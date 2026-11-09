#Requires -Version 5.1
<#
.SYNOPSIS
    Scenario de panne : arrete inventory-service, verifie que la confirmation d'une commande
    renvoie un 503 propre tandis que le catalogue et les livraisons continuent de repondre,
    puis redemarre le service et verifie que la confirmation reussit a nouveau.

.DESCRIPTION
    Etapes :
      1. connexion (Gestionnaire) et creation d'une commande en brouillon ;
      2. arret de inventory-service ;
      3. confirmation -> attendu : HTTP 503, errorCode SERVICE_UNAVAILABLE, dependency inventory-service,
         commande toujours en Brouillon avec lastError renseigne ;
      4. non-regression : GET /api/products et GET /api/deliveries -> HTTP 200 ;
      5. redemarrage de inventory-service et attente de sa sante ;
      6. nouvelle confirmation -> attendu : HTTP 200, statut Confirmee ;
      7. annulation de la commande de test (libere le stock).

    Deux modes d'arret/redemarrage :
      - Docker Compose (defaut) : docker compose stop/start <service> ;
      - commandes personnalisees (-CommandeArret / -CommandeDemarrage) pour un environnement
        sans Docker (par exemple des processus dotnet lances localement).

    Code de sortie : 0 si toutes les verifications passent, 1 sinon.

.EXAMPLE
    .\tests\scenario-panne.ps1 -BaseUrl http://192.168.10.10
.EXAMPLE
    .\tests\scenario-panne.ps1 -BaseUrl http://localhost -CommandeArret 'pkill -f GestionCommandes.InventoryService' -CommandeDemarrage './start-inventory.sh'
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://192.168.10.10',
    [string]$Service = 'inventory-service',
    [string]$FichierCompose = (Join-Path $PSScriptRoot '..\docker-compose.yml'),
    [string]$CommandeArret,
    [string]$CommandeDemarrage,
    [int]$DelaiSecondes = 180,
    [string]$MotDePasse = 'Micro@2025!'
)

$ErrorActionPreference = 'Stop'
$resultats = [System.Collections.Generic.List[object]]::new()

function Verifier([string]$Nom, [bool]$Ok, [string]$Detail = '') {
    $resultats.Add([pscustomobject]@{ Verification = $Nom; Resultat = $(if ($Ok) { 'OK' } else { 'ECHEC' }); Detail = $Detail })
    $couleur = if ($Ok) { 'Green' } else { 'Red' }
    Write-Host ("[{0}] {1} {2}" -f $(if ($Ok) { 'OK' } else { 'ECHEC' }), $Nom, $Detail) -ForegroundColor $couleur
}

# Appel HTTP qui renvoie le code de statut ET le corps, meme pour une erreur 4xx/5xx.
function Appeler([string]$Methode, [string]$Chemin, $Corps = $null, [hashtable]$Entetes = @{}) {
    $parametres = @{
        Method = $Methode
        Uri = "$BaseUrl$Chemin"
        Headers = $Entetes
        ContentType = 'application/json'
        UseBasicParsing = $true
        TimeoutSec = 30
    }
    if ($null -ne $Corps) { $parametres.Body = ($Corps | ConvertTo-Json -Depth 8) }

    try {
        $reponse = Invoke-WebRequest @parametres
        return [pscustomobject]@{
            Statut = [int]$reponse.StatusCode
            Corps = if ($reponse.Content) { $reponse.Content | ConvertFrom-Json } else { $null }
        }
    } catch {
        $statut = 0
        if ($_.Exception.Response) { $statut = [int]$_.Exception.Response.StatusCode }
        $texte = $_.ErrorDetails.Message
        $corps = $null
        if ($texte) { try { $corps = $texte | ConvertFrom-Json } catch { $corps = $null } }
        return [pscustomobject]@{ Statut = $statut; Corps = $corps }
    }
}

function Arreter-Service {
    if ($CommandeArret) { Invoke-Expression $CommandeArret; return }
    docker compose -f $FichierCompose stop $Service | Out-Null
}

function Demarrer-Service {
    if ($CommandeDemarrage) { Invoke-Expression $CommandeDemarrage; return }
    docker compose -f $FichierCompose start $Service | Out-Null
}

function Attendre-Sante {
    $fin = (Get-Date).AddSeconds($DelaiSecondes)
    do {
        Start-Sleep -Seconds 3
        $sante = Appeler GET '/health/services'
        $etat = $sante.Corps.services | Where-Object { $_.service -eq $Service }
        if ($etat -and $etat.healthy) { return $true }
    } until ((Get-Date) -gt $fin)
    return $false
}

# ---------------------------------------------------------------------------
Write-Host "`n=== Scenario de panne : $Service ===" -ForegroundColor Cyan
Write-Host "Plateforme : $BaseUrl"

Write-Host "`n[1] Connexion et commande de test" -ForegroundColor Cyan
$connexion = Appeler POST '/api/auth/login' @{ userName = 'gestion@micro.local'; password = $MotDePasse }
if ($connexion.Statut -ne 200) { throw "Connexion impossible (HTTP $($connexion.Statut)). Verifiez la plateforme." }
$auth = @{ Authorization = "Bearer $($connexion.Corps.accessToken)" }

$creation = Appeler POST '/api/orders' @{
    customerId = '33333333-0000-0000-0000-000000000001'
    lines = @(@{ productId = '22222222-0000-0000-0000-000000000001'; quantity = 1 })
} $auth
if ($creation.Statut -ne 201) { throw "Creation de la commande de test impossible (HTTP $($creation.Statut))." }
$commandeId = $creation.Corps.id
$numero = $creation.Corps.number
Write-Host "Commande de test : $numero (brouillon)"

Write-Host "`n[2] Arret de $Service" -ForegroundColor Cyan
Arreter-Service
Start-Sleep -Seconds 5

Write-Host "`n[3] Confirmation pendant la panne (503 attendu)" -ForegroundColor Cyan
$confirmation = Appeler POST "/api/orders/$commandeId/confirm" $null $auth
Verifier 'Confirmation renvoie HTTP 503' ($confirmation.Statut -eq 503) "(recu $($confirmation.Statut))"
Verifier 'Code d''erreur SERVICE_UNAVAILABLE' ($confirmation.Corps.errorCode -eq 'SERVICE_UNAVAILABLE') "(recu $($confirmation.Corps.errorCode))"
Verifier 'Dependance nommee : inventory-service' ($confirmation.Corps.dependency -eq 'inventory-service') "(recu $($confirmation.Corps.dependency))"
Verifier 'Reponse au format ProblemDetails (correlationId present)' ([bool]$confirmation.Corps.correlationId)

$apres = Appeler GET "/api/orders/$commandeId" $null $auth
Verifier 'Commande toujours en Brouillon' ($apres.Corps.status -eq 'Brouillon') "(etat $($apres.Corps.status))"
Verifier 'Motif de l''echec enregistre (lastError)' ([bool]$apres.Corps.lastError)

Write-Host "`n[4] Non-regression : le reste de la plateforme repond" -ForegroundColor Cyan
$produits = Appeler GET '/api/products?pageSize=3' $null $auth
Verifier 'Catalogue : GET /api/products -> 200' ($produits.Statut -eq 200) "(HTTP $($produits.Statut))"
$livraisons = Appeler GET '/api/deliveries?pageSize=3' $null $auth
Verifier 'Livraisons : GET /api/deliveries -> 200' ($livraisons.Statut -eq 200) "(HTTP $($livraisons.Statut))"
$sante = Appeler GET '/health/services'
$inventaire = $sante.Corps.services | Where-Object { $_.service -eq $Service }
Verifier "Sante agregee : $Service signale en panne" (-not $inventaire.healthy) "(healthy=$($inventaire.healthy))"

Write-Host "`n[5] Redemarrage de $Service" -ForegroundColor Cyan
Demarrer-Service
$retabli = Attendre-Sante
Verifier "$Service de nouveau sain dans les $DelaiSecondes s" $retabli

Write-Host "`n[6] Confirmation apres reprise (200 attendu)" -ForegroundColor Cyan
$reconfirmation = Appeler POST "/api/orders/$commandeId/confirm" $null $auth
Verifier 'Confirmation renvoie HTTP 200' ($reconfirmation.Statut -eq 200) "(recu $($reconfirmation.Statut))"
Verifier 'Commande Confirmee' ($reconfirmation.Corps.status -eq 'Confirmee') "(etat $($reconfirmation.Corps.status))"

Write-Host "`n[7] Nettoyage : annulation de la commande de test" -ForegroundColor Cyan
$annulation = Appeler POST "/api/orders/$commandeId/cancel" @{ reason = 'Fin du scenario de panne' } $auth
Verifier 'Annulation libere le stock (HTTP 200)' ($annulation.Statut -eq 200) "(HTTP $($annulation.Statut))"

# ---------------------------------------------------------------------------
Write-Host "`n=== Resume ===" -ForegroundColor Cyan
$resultats | Format-Table -AutoSize
$echecs = @($resultats | Where-Object Resultat -eq 'ECHEC').Count
if ($echecs -eq 0) {
    Write-Host 'Scenario de panne REUSSI : degradation propre et reprise verifiees.' -ForegroundColor Green
    exit 0
}
Write-Host "Scenario de panne : $echecs verification(s) en echec." -ForegroundColor Red
exit 1

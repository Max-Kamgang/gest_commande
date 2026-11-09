<#
.SYNOPSIS
    Construit les images, demarre les cinq conteneurs et verifie que la
    plateforme repond.

.DESCRIPTION
    A lancer depuis la racine du projet (le dossier qui contient
    docker-compose.yml). Le script :

      1. verifie la presence du fichier .env et de ses deux secrets,
      2. construit les images et demarre les conteneurs,
      3. attend que les cinq conteneurs soient « healthy »,
      4. interroge /health/services et affiche l'etat de chaque service.

    Rejouable : relance la construction et remplace les conteneurs modifies.

.PARAMETER SansBuild
    Demarre les conteneurs sans reconstruire les images.

.PARAMETER DelaiSecondes
    Duree maximale d'attente du demarrage. 300 secondes par defaut (la premiere
    construction telecharge les images de base, ce qui est long).

.EXAMPLE
    .\deploy\2-deployer.ps1
    .\deploy\2-deployer.ps1 -SansBuild
#>
[CmdletBinding()]
param(
    [switch]$SansBuild,
    [int]$DelaiSecondes = 300
)

$ErrorActionPreference = 'Stop'

function Etape([string]$Texte) {
    Write-Host ''
    Write-Host "=== $Texte ===" -ForegroundColor Cyan
}

# --- 1. Controles prealables ------------------------------------------------
Etape '1/4 Controles'

if (-not (Test-Path 'docker-compose.yml')) {
    throw 'Lancez ce script depuis la racine du projet (le dossier contenant docker-compose.yml).'
}

if (-not (Test-Path '.env')) {
    throw "Fichier .env absent. Executez : Copy-Item .env.example .env puis renseignez les secrets."
}

$env:COMPOSE_CONVERT_WINDOWS_PATHS = '0'
$configuration = Get-Content '.env' | Where-Object { $_ -match '^\s*[A-Z_]+=' }

foreach ($cle in @('JWT_KEY', 'INTERNAL_API_KEY')) {
    $ligne = $configuration | Where-Object { $_ -like "$cle=*" } | Select-Object -First 1
    $valeur = if ($ligne) { $ligne.Split('=', 2)[1].Trim() } else { '' }

    if (-not $valeur -or $valeur -like 'CHANGEZ-MOI*') {
        throw "$cle n'est pas renseignee dans le fichier .env."
    }

    if ($cle -eq 'JWT_KEY' -and $valeur.Length -lt 32) {
        throw "JWT_KEY doit faire au moins 32 caracteres (actuellement $($valeur.Length))."
    }
}

Write-Host '    [OK] Fichier .env valide.' -ForegroundColor Green

$portLigne = $configuration | Where-Object { $_ -like 'HTTP_PORT=*' } | Select-Object -First 1
$port = if ($portLigne) { $portLigne.Split('=', 2)[1].Trim() } else { '80' }

# --- 2. Construction et demarrage -------------------------------------------
Etape '2/4 Construction et demarrage'

if (-not $SansBuild) {
    Write-Host '    Construction des cinq images (long au premier lancement)...' -ForegroundColor Gray
    docker compose build

    if ($LASTEXITCODE -ne 0) { throw 'La construction des images a echoue.' }
}

docker compose up -d

if ($LASTEXITCODE -ne 0) { throw 'Le demarrage des conteneurs a echoue.' }

# --- 3. Attente de l'etat « healthy » ---------------------------------------
Etape '3/4 Attente du demarrage'

$services = @('identity-service', 'catalog-service', 'order-service', 'delivery-service', 'gateway')
$chrono = [Diagnostics.Stopwatch]::StartNew()
$pret = $false

while ($chrono.Elapsed.TotalSeconds -lt $DelaiSecondes) {
    $etats = @{}

    foreach ($service in $services) {
        $conteneur = docker compose ps -q $service

        $etats[$service] = if ($conteneur) {
            docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' $conteneur
        } else {
            'absent'
        }
    }

    $enLigne = ($etats.Values | Where-Object { $_ -eq 'healthy' }).Count
    Write-Host "    $enLigne/$($services.Count) conteneurs prets..." -ForegroundColor Gray

    if ($enLigne -eq $services.Count) {
        $pret = $true
        break
    }

    Start-Sleep -Seconds 10
}

if (-not $pret) {
    Write-Host ''
    Write-Warning "Tous les conteneurs ne sont pas prets apres $DelaiSecondes secondes."
    docker compose ps
    Write-Host ''
    Write-Host 'Pour voir ce qui bloque :' -ForegroundColor Yellow
    Write-Host '    docker compose logs --tail 50'
    return
}

Write-Host '    [OK] Les cinq conteneurs sont prets.' -ForegroundColor Green

# --- 4. Verification applicative --------------------------------------------
Etape '4/4 Verification'

try {
    $sante = Invoke-RestMethod -Uri "http://localhost:$port/health/services" -TimeoutSec 15

    foreach ($service in $sante.services) {
        $couleur = if ($service.statut -eq 'Healthy') { 'Green' } else { 'Red' }
        Write-Host ("    {0,-20} {1}" -f $service.service, $service.statut) -ForegroundColor $couleur
    }

    Write-Host ''

    if ($sante.statut -eq 'Healthy') {
        Write-Host 'Plateforme operationnelle.' -ForegroundColor Green
    } else {
        Write-Warning "Plateforme degradee : $($sante.statut)"
    }
} catch {
    Write-Warning "La gateway ne repond pas sur le port $port : $($_.Exception.Message)"
    Write-Host '    Verifiez la regle de pare-feu et « docker compose logs gateway ».'
    return
}

$adresse = "http://$($env:COMPUTERNAME)"

if ($port -ne '80') { $adresse += ":$port" }

Write-Host ''
Write-Host 'Acces :' -ForegroundColor Cyan
Write-Host "    Interface web   $adresse"
Write-Host "    Documentation   $adresse/swagger"
Write-Host "    Etat detaille   $adresse/health/services"
Write-Host ''
Write-Host 'Comptes de demonstration (mot de passe : Demo@2026) :' -ForegroundColor Cyan
Write-Host '    admin@micro.local          acces total'
Write-Host '    gestionnaire@micro.local   catalogue, stock, commandes, livraisons'
Write-Host '    livreur@micro.local        ses livraisons uniquement'
Write-Host '    client@micro.local         ses commandes uniquement'
Write-Host ''

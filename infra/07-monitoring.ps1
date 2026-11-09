#Requires -Version 5.1
<#
.SYNOPSIS
    Supervision de la plateforme : etat des conteneurs, consommation, sante des services,
    espace disque. Affiche un resume lisible et peut journaliser le resultat.

.DESCRIPTION
    Sources :
      - docker ps / docker stats (conteneurs et ressources),
      - GET /health/services sur la gateway (sante de chaque microservice, agregee),
      - espace libre du disque C: (un seul disque sur le serveur).

.PARAMETER BaseUrl
    Adresse de la gateway vue depuis le serveur.

.PARAMETER Journaliser
    Ajoute le resume au fichier C:\microservices\logs\monitoring.log.
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost',
    [string]$Racine = 'C:\microservices',
    [switch]$Journaliser
)

$ErrorActionPreference = 'Continue'
$lignes = [System.Collections.Generic.List[string]]::new()
function Ajouter([string]$Texte) { $lignes.Add($Texte); Write-Host $Texte }

Ajouter ''
Ajouter "=== Supervision Gestion des commandes - $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ==="

Ajouter ''
Ajouter '--- Conteneurs ---'
docker ps --filter 'name=gestion-commandes' --format 'table {{.Names}}\t{{.Status}}' | ForEach-Object { Ajouter $_ }

Ajouter ''
Ajouter '--- Ressources (instantane) ---'
docker stats --no-stream --format 'table {{.Name}}\t{{.CPUPerc}}\t{{.MemUsage}}' | ForEach-Object { Ajouter $_ }

Ajouter ''
Ajouter '--- Sante des services (gateway, /health/services) ---'
$sante = $null
try {
    $sante = Invoke-RestMethod -Uri "$BaseUrl/health/services" -TimeoutSec 15
} catch {
    # Une erreur 503 (service(s) indisponible(s)) renvoie tout de meme le detail en JSON.
    if ($_.ErrorDetails.Message) {
        $sante = $_.ErrorDetails.Message | ConvertFrom-Json
    }
}

if ($sante) {
    Ajouter ("Etat global : {0} ({1}/{2} services joignables)" -f $sante.status, $sante.healthy, $sante.total)
    foreach ($service in $sante.services) {
        $etat = if ($service.healthy) { 'OK ' } else { 'KO ' }
        $detail = if ($service.error) { " - $($service.error)" } else { '' }
        Ajouter ("  [{0}] {1,-22} HTTP {2,-4} {3} ms{4}" -f $etat, $service.service, $service.statusCode, $service.elapsedMs, $detail)
    }
} else {
    Ajouter 'La gateway ne repond pas : verifiez le conteneur gateway (docker compose ps).'
}

Ajouter ''
Ajouter '--- Espace disque (C: unique) ---'
$disque = Get-PSDrive -Name C
$libreGo = [Math]::Round($disque.Free / 1GB, 1)
$totalGo = [Math]::Round(($disque.Used + $disque.Free) / 1GB, 1)
$pourcentage = [Math]::Round(100 * $disque.Free / ($disque.Used + $disque.Free), 0)
Ajouter "Libre : $libreGo Go / $totalGo Go ($pourcentage %)"
if ($pourcentage -lt 15) { Ajouter 'ATTENTION : moins de 15 % d''espace libre.' }

Ajouter ''
Ajouter '--- Taille des donnees ---'
if (Test-Path (Join-Path $Racine 'data')) {
    $taille = (Get-ChildItem -Path (Join-Path $Racine 'data') -Recurse -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    Ajouter ("Dossier data : {0:N1} Mo" -f ($taille / 1MB))
}

if ($Journaliser) {
    $journal = Join-Path $Racine 'logs\monitoring.log'
    New-Item -ItemType Directory -Path (Split-Path $journal) -Force | Out-Null
    Add-Content -Path $journal -Value $lignes
    Write-Host "`nResume ajoute a $journal"
}

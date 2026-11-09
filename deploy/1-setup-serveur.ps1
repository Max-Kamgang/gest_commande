<#
.SYNOPSIS
    Prepare le serveur Windows pour heberger la plateforme : Docker, dossiers,
    reseau et pare-feu. A executer UNE SEULE FOIS, en administrateur.

.DESCRIPTION
    Ce script remplace la dizaine de scripts d'administration de la version
    precedente. Il enchaine quatre etapes, chacune sans effet si elle est deja
    faite (il est donc rejouable sans risque) :

      1. installation du role Containers et de Docker (redemarrage si besoin),
      2. creation de l'arborescence C:\microservices,
      3. creation du reseau Docker « microservices »,
      4. ouverture du port HTTP dans le pare-feu.

.PARAMETER Port
    Port HTTP a ouvrir dans le pare-feu. 80 par defaut, comme dans le .env.

.PARAMETER Racine
    Dossier d'installation. C:\microservices par defaut.

.EXAMPLE
    .\1-setup-serveur.ps1
    .\1-setup-serveur.ps1 -Port 8080
#>
[CmdletBinding()]
param(
    [int]$Port = 80,
    [string]$Racine = 'C:\microservices'
)

$ErrorActionPreference = 'Stop'

function Etape([string]$Texte) {
    Write-Host ''
    Write-Host "=== $Texte ===" -ForegroundColor Cyan
}

function Info([string]$Texte) { Write-Host "    $Texte" -ForegroundColor Gray }
function Ok([string]$Texte) { Write-Host "    [OK] $Texte" -ForegroundColor Green }

# --- Verification des droits ------------------------------------------------
$identite = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()

if (-not $identite.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ce script doit etre lance dans une console PowerShell ouverte en tant qu'administrateur."
}

# ---------------------------------------------------------------------------
Etape '1/4 Docker et conteneurs Windows'
# ---------------------------------------------------------------------------
$redemarrageRequis = $false

$roleContainers = Get-WindowsFeature -Name Containers -ErrorAction SilentlyContinue

if ($roleContainers -and -not $roleContainers.Installed) {
    Info 'Installation du role Containers...'
    $resultat = Install-WindowsFeature -Name Containers

    if ($resultat.RestartNeeded -eq 'Yes') { $redemarrageRequis = $true }

    Ok 'Role Containers installe.'
} else {
    Ok 'Role Containers deja present.'
}

if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Info 'Installation de Docker via le fournisseur Microsoft...'

    Install-Module -Name DockerMsftProvider -Repository PSGallery -Force -Confirm:$false
    Install-Package -Name docker -ProviderName DockerMsftProvider -Force -Confirm:$false

    $redemarrageRequis = $true
    Ok 'Docker installe.'
} else {
    Ok "Docker deja installe : $((docker --version))"
}

if ($redemarrageRequis) {
    Write-Host ''
    Write-Warning 'Un redemarrage est necessaire. Redemarrez, puis relancez ce script pour terminer.'
    return
}

if ((Get-Service docker -ErrorAction SilentlyContinue).Status -ne 'Running') {
    Info 'Demarrage du service Docker...'
    Start-Service docker
}

Set-Service -Name docker -StartupType Automatic
Ok 'Service Docker demarre et automatique.'

# ---------------------------------------------------------------------------
Etape '2/4 Dossiers de la plateforme'
# ---------------------------------------------------------------------------
# Un dossier par service : les bases SQLite restent separees, comme les services.
$dossiers = @(
    "$Racine\app",
    "$Racine\data\identity-service",
    "$Racine\data\catalog-service",
    "$Racine\data\order-service",
    "$Racine\data\delivery-service",
    "$Racine\data\gateway",
    "$Racine\sauvegardes"
)

foreach ($dossier in $dossiers) {
    if (-not (Test-Path $dossier)) {
        New-Item -ItemType Directory -Path $dossier -Force | Out-Null
    }
}

Ok "$($dossiers.Count) dossiers prets sous $Racine."
Info "Code de l'application a deposer dans $Racine\app"

# ---------------------------------------------------------------------------
Etape '3/4 Reseau Docker'
# ---------------------------------------------------------------------------
# Driver « nat » : le reseau des conteneurs Windows. Son serveur DNS interne
# permet aux services de se joindre par leur nom (http://catalog-service:8080).
if (-not (docker network ls --filter name=^microservices$ --format '{{.Name}}')) {
    docker network create --driver nat microservices | Out-Null
    Ok 'Reseau « microservices » cree.'
} else {
    Ok 'Reseau « microservices » deja present.'
}

# ---------------------------------------------------------------------------
Etape '4/4 Pare-feu'
# ---------------------------------------------------------------------------
# Seul le port de la gateway est ouvert. Les quatre services restent internes
# au reseau Docker : ils ne sont joignables depuis aucun poste du reseau.
$regle = "Gestion commandes - HTTP $Port"

if (-not (Get-NetFirewallRule -DisplayName $regle -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName $regle `
        -Direction Inbound -Action Allow -Protocol TCP -LocalPort $Port -Profile Any | Out-Null

    Ok "Port $Port ouvert en entree."
} else {
    Ok "Regle de pare-feu deja presente pour le port $Port."
}

# ---------------------------------------------------------------------------
Write-Host ''
Write-Host 'Serveur pret.' -ForegroundColor Green
Write-Host ''
Write-Host 'Etapes suivantes :' -ForegroundColor Cyan
Write-Host "  1. Copier le code du projet dans $Racine\app"
Write-Host "  2. cd $Racine\app"
Write-Host '  3. Copy-Item .env.example .env   puis renseigner JWT_KEY et INTERNAL_API_KEY'
Write-Host '  4. .\deploy\2-deployer.ps1'
Write-Host ''

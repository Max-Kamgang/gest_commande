#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Verifie l'installation de Docker CE (moteur Windows) et de Docker Compose.

.DESCRIPTION
    Docker CE et Docker Compose sont DEJA installes sur SRV-MICRO-01 : ce script documente
    l'installation, controle la configuration et n'installe le moteur que s'il manque
    (module DockerMsftProvider, depot PSGallery). Idempotent.
#>
[CmdletBinding()]
param(
    [switch]$Installer    # sans ce commutateur, le script se contente de verifier
)

$ErrorActionPreference = 'Stop'

Write-Host "`n==> Moteur Docker" -ForegroundColor Cyan
$service = Get-Service -Name docker -ErrorAction SilentlyContinue

if (-not $service) {
    if (-not $Installer) {
        throw 'Docker est absent. Relancez avec -Installer pour installer le moteur (acces Internet requis).'
    }
    Write-Host 'Installation du moteur Docker CE via DockerMsftProvider...'
    Install-Module -Name DockerMsftProvider -Repository PSGallery -Force
    Install-Package -Name docker -ProviderName DockerMsftProvider -Force
    Write-Warning 'Redemarrez le serveur, puis relancez ce script pour verifier la configuration.'
    return
}

if ($service.Status -ne 'Running') {
    Start-Service -Name docker
    Write-Host 'Service docker demarre.'
}
Set-Service -Name docker -StartupType Automatic
Write-Host "Service docker : $((Get-Service docker).Status), demarrage automatique."

Write-Host "`n==> Version et mode" -ForegroundColor Cyan
docker version --format 'Client {{.Client.Version}} / Serveur {{.Server.Version}}'
$osType = docker info --format '{{.OSType}}'
if ($osType -ne 'windows') {
    throw "Le moteur Docker repond en « $osType » : il doit fonctionner en mode conteneurs Windows."
}
Write-Host 'Mode conteneurs Windows : OK.' -ForegroundColor Green

Write-Host "`n==> Docker Compose" -ForegroundColor Cyan
$composeVersion = docker compose version 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'Le plugin « docker compose » est introuvable. Installez Docker Compose v2.'
}
Write-Host $composeVersion -ForegroundColor Green

Write-Host "`n==> Repertoire de stockage Docker" -ForegroundColor Cyan
docker info --format 'Racine Docker : {{.DockerRootDir}}'
Write-Host 'Le disque C: est unique : surveillez l''espace libre (voir 07-monitoring.ps1).'

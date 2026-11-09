#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    OPTION : installe le role DNS et cree la zone micro.local (enregistrements A et CNAME).

.DESCRIPTION
    Permet aux clients d'utiliser http://app.micro.local au lieu de l'adresse IP.
    La zone est creee en zone primaire fichier (pas de domaine Active Directory).
    Idempotent : le role, la zone et les enregistrements ne sont crees que s'ils manquent.
#>
[CmdletBinding()]
param(
    [string]$Zone = 'micro.local',
    [string]$AdresseIP = '192.168.10.10',
    [string]$NomHote = 'srv-micro-01'
)

$ErrorActionPreference = 'Stop'

Write-Host "`n==> Role DNS" -ForegroundColor Cyan
$role = Get-WindowsFeature -Name DNS
if (-not $role.Installed) {
    Install-WindowsFeature -Name DNS -IncludeManagementTools | Out-Null
    Write-Host 'Role DNS installe.'
} else {
    Write-Host 'Role DNS deja installe.'
}

Write-Host "`n==> Zone $Zone" -ForegroundColor Cyan
if (-not (Get-DnsServerZone -Name $Zone -ErrorAction SilentlyContinue)) {
    Add-DnsServerPrimaryZone -Name $Zone -ZoneFile "$Zone.dns"
    Write-Host "Zone $Zone creee."
} else {
    Write-Host "Zone $Zone deja presente."
}

Write-Host "`n==> Enregistrements" -ForegroundColor Cyan
if (-not (Get-DnsServerResourceRecord -ZoneName $Zone -Name $NomHote -RRType A -ErrorAction SilentlyContinue)) {
    Add-DnsServerResourceRecordA -ZoneName $Zone -Name $NomHote -IPv4Address $AdresseIP
    Write-Host "A : $NomHote.$Zone -> $AdresseIP"
} else {
    Write-Host "A : $NomHote.$Zone existe deja."
}

if (-not (Get-DnsServerResourceRecord -ZoneName $Zone -Name 'app' -RRType CName -ErrorAction SilentlyContinue)) {
    Add-DnsServerResourceRecordCName -ZoneName $Zone -Name 'app' -HostNameAlias "$NomHote.$Zone."
    Write-Host "CNAME : app.$Zone -> $NomHote.$Zone."
} else {
    Write-Host 'CNAME : app existe deja.'
}

Write-Host "`nVerification depuis le client : Resolve-DnsName app.$Zone" -ForegroundColor Green
Write-Host "Configurez le DNS du client Windows 10 sur $AdresseIP."

#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Ouvre uniquement les ports necessaires a la plateforme : 80 et 443 (gateway),
    et, en option, les ports du mode Swarm.

.DESCRIPTION
    Principe de moindre exposition : aucun port de service (5000-5010 en local, 8080 dans
    les conteneurs) n'est ouvert. Les services ne sont joignables que par la gateway.
    Les regles sont nommees « MICRO-... » et recreees a chaque execution (idempotent).

.PARAMETER AvecSwarm
    Ajoute les ports Swarm : 2377/TCP (gestion), 7946/TCP+UDP (gossip), 4789/UDP (overlay).

.EXAMPLE
    .\03-firewall.ps1 -AvecSwarm
#>
[CmdletBinding()]
param(
    [switch]$AvecSwarm,
    [string]$SousReseau = 'LocalSubnet'   # seul le reseau local du projet (192.168.10.0/24) est admis
)

$ErrorActionPreference = 'Stop'

function Regle([string]$Nom, [string]$Description, [string]$Protocole, [int[]]$Ports) {
    Get-NetFirewallRule -DisplayName $Nom -ErrorAction SilentlyContinue | Remove-NetFirewallRule
    New-NetFirewallRule -DisplayName $Nom -Description $Description -Direction Inbound `
        -Protocol $Protocole -LocalPort $Ports -RemoteAddress $SousReseau `
        -Action Allow -Profile Any | Out-Null
    Write-Host "Regle '$Nom' : $Protocole $($Ports -join ',') depuis $SousReseau"
}

Write-Host "`n==> Ports de la gateway" -ForegroundColor Cyan
Regle -Nom 'MICRO-HTTP-80-Entrant' -Description 'Gateway : HTTP (interface web et API).' -Protocole TCP -Ports 80
Regle -Nom 'MICRO-HTTPS-443-Entrant' -Description 'Gateway : HTTPS (certificat auto-signe).' -Protocole TCP -Ports 443

if ($AvecSwarm) {
    Write-Host "`n==> Ports Swarm (option bonus)" -ForegroundColor Cyan
    Regle -Nom 'MICRO-SWARM-Gestion-2377' -Description 'Swarm : communication de gestion.' -Protocole TCP -Ports 2377
    Regle -Nom 'MICRO-SWARM-Gossip-7946-TCP' -Description 'Swarm : decouverte des noeuds (TCP).' -Protocole TCP -Ports 7946
    Regle -Nom 'MICRO-SWARM-Gossip-7946-UDP' -Description 'Swarm : decouverte des noeuds (UDP).' -Protocole UDP -Ports 7946
    Regle -Nom 'MICRO-SWARM-Overlay-4789' -Description 'Swarm : trafic du reseau overlay (VXLAN).' -Protocole UDP -Ports 4789
}

Write-Host "`n==> Regles MICRO-* actives" -ForegroundColor Cyan
Get-NetFirewallRule -DisplayName 'MICRO-*' |
    Select-Object DisplayName, Enabled, Direction, Action |
    Format-Table -AutoSize

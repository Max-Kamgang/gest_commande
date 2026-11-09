#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Verifie et active la fonctionnalite Windows « Containers » (isolation processus).

.DESCRIPTION
    Document d'installation : sur SRV-MICRO-01 cette etape est deja realisee. Le script
    confirme l'etat et n'installe que si la fonctionnalite manque. Idempotent.
    Les conteneurs utilisent l'isolation PROCESSUS (images nanoserver, meme noyau que l'hote).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

Write-Host "`n==> Fonctionnalite Windows Containers" -ForegroundColor Cyan
$fonctionnalite = Get-WindowsFeature -Name Containers

if ($fonctionnalite.Installed) {
    Write-Host 'La fonctionnalite Containers est deja installee.' -ForegroundColor Green
} else {
    Write-Host 'Installation de la fonctionnalite Containers...'
    $resultat = Install-WindowsFeature -Name Containers
    if ($resultat.RestartNeeded -eq 'Yes') {
        Write-Warning 'Un redemarrage est requis. Relancez ce script puis 02-install-docker.ps1 apres le redemarrage.'
    } else {
        Write-Host 'Fonctionnalite installee.' -ForegroundColor Green
    }
}

Write-Host "`n==> Isolation par defaut" -ForegroundColor Cyan
Write-Host 'Les conteneurs de ce projet utilisent l''isolation processus (process). Verifiez avec :'
Write-Host '  docker info --format "{{.Isolation}}"   (doit afficher « process » ou etre vide)'

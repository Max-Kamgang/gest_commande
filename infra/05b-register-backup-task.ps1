#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Planifie la sauvegarde nocturne (05-backup.ps1) dans le Planificateur de taches.

.DESCRIPTION
    Tache « MICRO-Sauvegarde-Nocturne », chaque nuit a 02:30, sous le compte SYSTEM,
    avec une duree maximale de 2 heures. Idempotent : la tache est recreee a chaque
    execution, sans doublon.
#>
[CmdletBinding()]
param(
    [string]$Racine = 'C:\microservices',
    [string]$Heure = '02:30'
)

$ErrorActionPreference = 'Stop'

$nomTache = 'MICRO-Sauvegarde-Nocturne'
$script = Join-Path $Racine 'app\infra\05-backup.ps1'

if (-not (Test-Path $script)) {
    throw "Script introuvable : $script. Clonez le depot dans $Racine\app avant d'executer ce script."
}

$action = New-ScheduledTaskAction -Execute 'powershell.exe' `
    -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$script`" -Racine `"$Racine`""
$declencheur = New-ScheduledTaskTrigger -Daily -At $Heure
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$parametres = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 2) `
    -MultipleInstances IgnoreNew

Register-ScheduledTask -TaskName $nomTache -Action $action -Trigger $declencheur `
    -Principal $principal -Settings $parametres `
    -Description 'Sauvegarde nocturne des bases SQLite et des journaux de la plateforme.' `
    -Force | Out-Null

Write-Host "Tache '$nomTache' planifiee chaque jour a $Heure." -ForegroundColor Green
Get-ScheduledTask -TaskName $nomTache | Get-ScheduledTaskInfo |
    Select-Object TaskName, NextRunTime, LastRunTime, LastTaskResult | Format-List

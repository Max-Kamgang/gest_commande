#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Sauvegarde horodatee des bases SQLite et des journaux (archive ZIP), avec rotation.

.DESCRIPTION
    - Une base par service : data\<service>\<service>.db
    - Les journaux de chaque service : data\<service>\logs\
    - Si sqlite3.exe est present (C:\microservices\tools\sqlite3.exe ou dans le PATH),
      la copie se fait avec la commande .backup de SQLite : copie coherente meme pendant
      l'ecriture. Sinon, la copie brute est faite en partage de lecture.
    - Rotation : seules les N dernieres archives sont conservees (7 par defaut).

.PARAMETER Conservation
    Nombre d'archives a conserver.
#>
[CmdletBinding()]
param(
    [string]$Racine = 'C:\microservices',
    [int]$Conservation = 7
)

$ErrorActionPreference = 'Stop'

$dossierSauvegardes = Join-Path $Racine 'backups'
$horodatage = Get-Date -Format 'yyyyMMdd-HHmmss'
$temporaire = Join-Path $dossierSauvegardes "tmp-$horodatage"
$archive = Join-Path $dossierSauvegardes "micro-$horodatage.zip"

$sqlite = Get-Command sqlite3.exe -ErrorAction SilentlyContinue
if (-not $sqlite -and (Test-Path (Join-Path $Racine 'tools\sqlite3.exe'))) {
    $sqlite = Join-Path $Racine 'tools\sqlite3.exe'
}

New-Item -ItemType Directory -Path $temporaire -Force | Out-Null
Write-Host "Sauvegarde du $horodatage"

try {
    foreach ($base in Get-ChildItem -Path (Join-Path $Racine 'data') -Recurse -Filter '*.db' -File) {
        $service = $base.Directory.Name
        $destination = Join-Path $temporaire "$service\$($base.Name)"
        New-Item -ItemType Directory -Path (Split-Path $destination) -Force | Out-Null

        if ($sqlite) {
            & $sqlite $base.FullName ".backup '$destination'"
            if ($LASTEXITCODE -ne 0) { throw "Echec de .backup pour $($base.FullName)" }
            Write-Host "  base $service (sqlite3 .backup)"
        } else {
            # Lecture partagee : SQLite ecrit en mode journal DELETE ; la copie est coherente
            # hors ecriture en cours. Installer sqlite3.exe pour une copie garantie.
            $source = [System.IO.File]::Open($base.FullName, 'Open', 'Read', 'ReadWrite')
            try {
                $cible = [System.IO.File]::Create($destination)
                try { $source.CopyTo($cible) } finally { $cible.Dispose() }
            } finally { $source.Dispose() }
            Write-Host "  base $service (copie brute, sqlite3 absent)"
        }
    }

    foreach ($journaux in Get-ChildItem -Path (Join-Path $Racine 'data') -Recurse -Directory -Filter 'logs') {
        $destination = Join-Path $temporaire ("journaux\" + $journaux.Parent.Name)
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Copy-Item -Path (Join-Path $journaux.FullName '*') -Destination $destination -Force -ErrorAction SilentlyContinue
    }

    Compress-Archive -Path (Join-Path $temporaire '*') -DestinationPath $archive -CompressionLevel Optimal -Force
    Write-Host "Archive creee : $archive" -ForegroundColor Green
}
finally {
    Remove-Item -Path $temporaire -Recurse -Force -ErrorAction SilentlyContinue
}

# Rotation : on garde les N archives les plus recentes.
$anciennes = Get-ChildItem -Path $dossierSauvegardes -Filter 'micro-*.zip' -File |
    Sort-Object LastWriteTime -Descending |
    Select-Object -Skip $Conservation
foreach ($ancienne in $anciennes) {
    Remove-Item -Path $ancienne.FullName -Force
    Write-Host "Rotation : supprime $($ancienne.Name)"
}
Write-Host "Archives conservees : $([Math]::Min($Conservation, @(Get-ChildItem $dossierSauvegardes -Filter 'micro-*.zip').Count))"

#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Genere le certificat auto-signe de la gateway et l'exporte au format .pfx (monte dans
    le conteneur) et .cer (a importer sur le poste client).

.DESCRIPTION
    - Cle RSA 2048, validite 2 ans, SAN : SRV-MICRO-01, localhost, 192.168.10.10.
    - Le mot de passe du .pfx est lu dans le fichier .env (variable CERT_PASSWORD) s'il
      existe, sinon demande de facon securisee.
    - Idempotent : si gateway.pfx et gateway.cer existent deja, rien n'est regenere.
      Pour renouveler, supprimez-les puis relancez le script.

    Sur le poste Windows 10 client : importer gateway.cer dans « Autorites de certification
    racines de confiance » (certlm.msc ou certmgr.msc), sinon le navigateur signale le certificat.
#>
[CmdletBinding()]
param(
    [string]$Racine = 'C:\microservices',
    [string]$Dns = 'SRV-MICRO-01,localhost',
    [string]$AdresseIP = '192.168.10.10',
    [int]$ValiditeAns = 2,
    [string]$FichierEnv = (Join-Path $PSScriptRoot '..\.env')
)

$ErrorActionPreference = 'Stop'

$dossier = Join-Path $Racine 'certs'
$pfx = Join-Path $dossier 'gateway.pfx'
$cer = Join-Path $dossier 'gateway.cer'

New-Item -ItemType Directory -Path $dossier -Force | Out-Null

if ((Test-Path $pfx) -and (Test-Path $cer)) {
    Write-Host 'Certificat deja present : aucune action (supprimez gateway.pfx et gateway.cer pour le renouveler).' -ForegroundColor Yellow
    return
}

# Mot de passe : fichier .env en priorite, sinon saisie securisee.
$motDePasse = $null
if (Test-Path $FichierEnv) {
    $ligne = Get-Content $FichierEnv | Where-Object { $_ -match '^\s*CERT_PASSWORD\s*=' } | Select-Object -First 1
    if ($ligne) { $motDePasse = ($ligne -split '=', 2)[1].Trim() }
}
if (-not $motDePasse) {
    $securise = Read-Host 'Mot de passe du fichier .pfx' -AsSecureString
} else {
    $securise = ConvertTo-SecureString -String $motDePasse -AsPlainText -Force
}

# SAN : noms DNS et adresse IP (les navigateurs exigent le SAN, plus le CN).
$dnsListe = $Dns -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }
$texte = '2.5.29.17={text}' + (($dnsListe | ForEach-Object { "DNS=$_" }) -join '&') + "&IPAddress=$AdresseIP"

$certificat = New-SelfSignedCertificate `
    -Subject 'CN=SRV-MICRO-01 - Gestion des commandes' `
    -TextExtension @($texte) `
    -CertStoreLocation 'Cert:\LocalMachine\My' `
    -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 `
    -NotAfter (Get-Date).AddYears($ValiditeAns) `
    -FriendlyName 'Gestion commandes - gateway'

Export-PfxCertificate -Cert $certificat -FilePath $pfx -Password $securise | Out-Null
Export-Certificate -Cert $certificat -FilePath $cer -Type CERT | Out-Null

Write-Host "Certificat genere (empreinte $($certificat.Thumbprint))." -ForegroundColor Green
Write-Host "  PFX (conteneur gateway) : $pfx"
Write-Host "  CER (poste client)      : $cer"
Write-Host 'Autorisez le groupe Utilisateurs en lecture sur ce dossier (voir 04-users-ntfs.ps1).'

#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Cree le compte de service svc-micro, le groupe MicroservicesAdmins, l'arborescence
    C:\microservices et les permissions NTFS minimales (icacls).

.DESCRIPTION
    Modele de droits :
      - SYSTEM et Administrateurs : controle total partout.
      - MicroservicesAdmins       : modification de l'arborescence (exploitation humaine).
      - svc-micro                 : compte de service local des taches planifiees
                                    (sauvegarde). Il n'est membre d'aucun groupe d'administration.
      - Dossiers data\<service>   : modification pour le groupe Utilisateurs, dont
                                    fait partie le compte ContainerUser des conteneurs Windows.
                                    Chaque service n'a acces qu'a SON dossier.
      - certs                     : lecture seule pour les conteneurs (gateway.pfx).

    Les SID sont utilises a la place des noms de groupes : ils sont independants de la
    langue du systeme (Administrateurs / Administrators...).

    Idempotent : les ACL sont reappliquees a chaque execution, et l'utilisateur n'est cree
    que s'il n'existe pas.

.PARAMETER MotDePasseSvc
    Mot de passe du compte svc-micro. Si absent, il est demande de facon securisee.
#>
[CmdletBinding()]
param(
    [string]$Racine = 'C:\microservices',
    [securestring]$MotDePasseSvc
)

$ErrorActionPreference = 'Stop'

# SID independants de la langue : SYSTEM, Administrateurs, Utilisateurs.
$sidSysteme = '*S-1-5-18'
$sidAdmins = '*S-1-5-32-544'
$sidUtilisateurs = '*S-1-5-32-545'

$services = 'identity-service', 'catalog-service', 'inventory-service', 'order-service',
            'delivery-service', 'notification-service', 'gateway', 'web-client'

Write-Host "`n==> Compte de service svc-micro" -ForegroundColor Cyan
if (Get-LocalUser -Name 'svc-micro' -ErrorAction SilentlyContinue) {
    Write-Host 'Le compte svc-micro existe deja.'
} else {
    if (-not $MotDePasseSvc) {
        $MotDePasseSvc = Read-Host 'Mot de passe du compte svc-micro' -AsSecureString
    }
    New-LocalUser -Name 'svc-micro' -Password $MotDePasseSvc -PasswordNeverExpires `
        -UserMayNotChangePassword -Description 'Compte de service : sauvegardes planifiees' | Out-Null
    Write-Host 'Compte svc-micro cree.'
}

Write-Host "`n==> Groupe MicroservicesAdmins" -ForegroundColor Cyan
if (-not (Get-LocalGroup -Name 'MicroservicesAdmins' -ErrorAction SilentlyContinue)) {
    New-LocalGroup -Name 'MicroservicesAdmins' -Description 'Exploitants de la plateforme microservices' | Out-Null
    Write-Host 'Groupe MicroservicesAdmins cree.'
} else {
    Write-Host 'Le groupe MicroservicesAdmins existe deja.'
}
# Les membres sont geres a la main : le script ne retire jamais un membre existant.

Write-Host "`n==> Arborescence" -ForegroundColor Cyan
$dossiers = @(
    "$Racine\app", "$Racine\infra", "$Racine\backups", "$Racine\logs", "$Racine\certs", "$Racine\data"
) + ($services | ForEach-Object { "$Racine\data\$_" })

foreach ($dossier in $dossiers) {
    if (-not (Test-Path $dossier)) {
        New-Item -ItemType Directory -Path $dossier -Force | Out-Null
        Write-Host "Cree : $dossier"
    }
}

Write-Host "`n==> Permissions NTFS (icacls)" -ForegroundColor Cyan

# Arborescence racine : rien pour les utilisateurs ordinaires.
icacls $Racine /inheritance:r `
    /grant:r "${sidSysteme}:(OI)(CI)F" "${sidAdmins}:(OI)(CI)F" "MicroservicesAdmins:(OI)(CI)M" `
    | Out-Null

# Sauvegardes et journaux : le compte de service y ecrit, les exploitants lisent et ecrivent.
foreach ($dossier in "$Racine\backups", "$Racine\logs") {
    icacls $dossier /inheritance:r `
        /grant:r "${sidSysteme}:(OI)(CI)F" "${sidAdmins}:(OI)(CI)F" `
        "MicroservicesAdmins:(OI)(CI)M" "svc-micro:(OI)(CI)M" | Out-Null
}

# Certificats : lecture seule pour les conteneurs de la gateway, jamais d'ecriture.
icacls "$Racine\certs" /inheritance:r `
    /grant:r "${sidSysteme}:(OI)(CI)F" "${sidAdmins}:(OI)(CI)F" `
    "MicroservicesAdmins:(OI)(CI)R" "${sidUtilisateurs}:(OI)(CI)R" | Out-Null

# Donnees : un dossier par service, modifiable par les conteneurs (groupe Utilisateurs).
icacls "$Racine\data" /inheritance:r `
    /grant:r "${sidSysteme}:(OI)(CI)F" "${sidAdmins}:(OI)(CI)F" "MicroservicesAdmins:(OI)(CI)M" | Out-Null
foreach ($service in $services) {
    icacls "$Racine\data\$service" /inheritance:r `
        /grant:r "${sidSysteme}:(OI)(CI)F" "${sidAdmins}:(OI)(CI)F" `
        "MicroservicesAdmins:(OI)(CI)M" "${sidUtilisateurs}:(OI)(CI)M" | Out-Null
}
Write-Host 'Permissions appliquees (voir icacls <chemin> pour verifier).'

Write-Host "`n==> Verification" -ForegroundColor Cyan
icacls "$Racine\data\identity-service"
Write-Host "`nSi un conteneur echoue avec « acces refuse » sur son dossier data, verifiez que le"
Write-Host 'groupe Utilisateurs y a bien la modification, puis redemarrez le service.'

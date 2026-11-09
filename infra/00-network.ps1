#Requires -RunAsAdministrator
#Requires -Version 5.1
<#
.SYNOPSIS
    Configure le reseau de SRV-MICRO-01 : IP statique, passerelle, DNS, nom de l'hote,
    et autorise le ping depuis le poste client Windows 10.

.DESCRIPTION
    Plan d'adressage (VMware, reseau NAT VMnet8) :
      - serveur  : 192.168.10.10/24  (SRV-MICRO-01, Windows Server 2025)
      - client   : 192.168.10.20     (Windows 10, navigateur + Postman)
      - passerelle et DNS : 192.168.10.2

    Le script est IDEMPOTENT : chaque etape verifie l'etat courant et n'agit que si
    la configuration differe de la cible. A executer de preference depuis la console
    VMware : la reconfiguration de la carte coupe une session RDP eventuelle.

.EXAMPLE
    .\00-network.ps1
#>
[CmdletBinding()]
param(
    [string]$NomAdaptateur,                       # facultatif, ex. "Ethernet0"
    [string]$AdresseIP = '192.168.10.10',
    [int]$Prefixe = 24,
    [string]$Passerelle = '192.168.10.2',
    [string[]]$ServeursDNS = @('192.168.10.2'),
    [string]$NomOrdinateur = 'SRV-MICRO-01',
    [string]$AdresseClient = '192.168.10.20'
)

$ErrorActionPreference = 'Stop'

function Ecrire-Etape([string]$Texte) {
    Write-Host "`n==> $Texte" -ForegroundColor Cyan
}

# ---------------------------------------------------------------------------
Ecrire-Etape "Selection de l'adaptateur reseau"
# Sans nom explicite, on prend la premiere carte active (la carte VMware unique du serveur).
if ($NomAdaptateur) {
    $adaptateur = Get-NetAdapter -Name $NomAdaptateur
} else {
    $adaptateur = Get-NetAdapter | Where-Object Status -eq 'Up' | Sort-Object ifIndex | Select-Object -First 1
}
if (-not $adaptateur) { throw 'Aucun adaptateur reseau actif. Verifiez la carte virtuelle dans VMware.' }
$alias = $adaptateur.Name
Write-Host "Adaptateur retenu : $alias"

# ---------------------------------------------------------------------------
Ecrire-Etape 'Adresse IP statique et passerelle'
$ipCourante = Get-NetIPAddress -InterfaceAlias $alias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.PrefixOrigin -ne 'WellKnown' }
$conforme = $ipCourante | Where-Object { $_.IPAddress -eq $AdresseIP -and $_.PrefixLength -eq $Prefixe }

if ($conforme) {
    Write-Host "Adresse $AdresseIP/$Prefixe deja configuree."
} else {
    # Retire une eventuelle configuration DHCP ou precedente avant d'appliquer la cible.
    Get-NetRoute -InterfaceAlias $alias -DestinationPrefix '0.0.0.0/0' -ErrorAction SilentlyContinue |
        Remove-NetRoute -Confirm:$false -ErrorAction SilentlyContinue
    Get-NetIPAddress -InterfaceAlias $alias -AddressFamily IPv4 -ErrorAction SilentlyContinue |
        Where-Object { $_.PrefixOrigin -ne 'WellKnown' } |
        Remove-NetIPAddress -Confirm:$false -ErrorAction SilentlyContinue

    New-NetIPAddress -InterfaceAlias $alias -IPAddress $AdresseIP -PrefixLength $Prefixe -DefaultGateway $Passerelle | Out-Null
    Write-Host "Adresse $AdresseIP/$Prefixe configuree, passerelle $Passerelle."
}

# ---------------------------------------------------------------------------
Ecrire-Etape 'Serveurs DNS'
$dnsCourants = (Get-DnsClientServerAddress -InterfaceAlias $alias -AddressFamily IPv4).ServerAddresses
if (($dnsCourants -join ',') -eq ($ServeursDNS -join ',')) {
    Write-Host "DNS deja conformes : $($ServeursDNS -join ', ')"
} else {
    Set-DnsClientServerAddress -InterfaceAlias $alias -ServerAddresses $ServeursDNS
    Write-Host "DNS definis : $($ServeursDNS -join ', ')"
}

# ---------------------------------------------------------------------------
Ecrire-Etape "Nom de l'ordinateur"
if ($env:COMPUTERNAME -eq $NomOrdinateur) {
    Write-Host "Le serveur s'appelle deja $NomOrdinateur."
} else {
    Rename-Computer -NewName $NomOrdinateur -Force
    Write-Warning "Renommage vers $NomOrdinateur : un REDEMARRAGE est necessaire pour l'appliquer."
}

# ---------------------------------------------------------------------------
Ecrire-Etape 'Pare-feu : ping autorise depuis le client Windows 10'
# Regle nommee et recreee a chaque execution : pas de doublon, meme si l'adresse change.
$nomRegle = 'MICRO-ICMPv4-Echo-Entrant-Client'
Get-NetFirewallRule -DisplayName $nomRegle -ErrorAction SilentlyContinue | Remove-NetFirewallRule
New-NetFirewallRule -DisplayName $nomRegle `
    -Description "Autorise le ping (echo ICMPv4) depuis le poste client $AdresseClient." `
    -Direction Inbound -Protocol ICMPv4 -IcmpType 8 `
    -RemoteAddress $AdresseClient -Action Allow -Profile Any | Out-Null
Write-Host "Regle '$nomRegle' active pour $AdresseClient."

# ---------------------------------------------------------------------------
Ecrire-Etape 'Verification de la connectivite'
if (Test-Connection -ComputerName $Passerelle -Count 2 -Quiet) {
    Write-Host "Passerelle $Passerelle joignable." -ForegroundColor Green
} else {
    Write-Warning "La passerelle $Passerelle ne repond pas : verifiez le reseau NAT de VMware (VMnet8)."
}

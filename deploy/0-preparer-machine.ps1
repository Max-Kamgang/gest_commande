<#
.SYNOPSIS
    Prepare une machine virtuelle neuve : nom, adresse reseau fixe, Docker,
    Docker Compose et Git. A lancer en administrateur sur chaque VM.

.DESCRIPTION
    Script unique pour les deux machines du projet :

      - Role « Serveur » : nom + IP fixe + Docker + Docker Compose + Git
      - Role « Client »  : nom + IP fixe + Git (pas de Docker)

    Chaque etape est sans effet si elle est deja faite : le script est rejouable
    sans risque. Un redemarrage peut etre demande (renommage, role Containers) :
    redemarrer puis relancer le script, il reprend ou il s'etait arrete.

.PARAMETER Role
    « Serveur » (defaut) ou « Client ».

.PARAMETER Nom
    Nom de la machine. Par defaut SRV-MICRO-01 ou PC-CLIENT-01 selon le role.

.PARAMETER Ip
    Adresse IPv4 fixe. Par defaut 192.168.10.10 (serveur) ou 192.168.10.20 (client).

.PARAMETER Passerelle
    Passerelle par defaut du reseau virtuel. 192.168.10.2 avec le NAT VMware.

.PARAMETER Dns
    Serveur DNS a utiliser pendant l'installation (il faut Internet pour
    telecharger Docker et Git). 192.168.10.2 par defaut.

.EXAMPLE
    # Sur la VM serveur
    .\0-preparer-machine.ps1

.EXAMPLE
    # Sur la VM cliente
    .\0-preparer-machine.ps1 -Role Client
#>
[CmdletBinding()]
param(
    [ValidateSet('Serveur', 'Client')]
    [string]$Role = 'Serveur',

    [string]$Nom,
    [string]$Ip,
    [int]$Prefixe = 24,
    [string]$Passerelle = '192.168.10.2',
    [string]$Dns = '192.168.10.2'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

# Valeurs par defaut dependant du role.
if (-not $Nom) { $Nom = if ($Role -eq 'Serveur') { 'SRV-MICRO-01' } else { 'PC-CLIENT-01' } }
if (-not $Ip) { $Ip = if ($Role -eq 'Serveur') { '192.168.10.10' } else { '192.168.10.20' } }

$redemarrageRequis = $false

function Etape([string]$Texte) {
    Write-Host ''
    Write-Host "=== $Texte ===" -ForegroundColor Cyan
}

function Info([string]$Texte) { Write-Host "    $Texte" -ForegroundColor Gray }
function Ok([string]$Texte) { Write-Host "    [OK] $Texte" -ForegroundColor Green }

# --- Droits administrateur --------------------------------------------------
$identite = [Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()

if (-not $identite.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Ouvrez PowerShell en tant qu'administrateur, puis relancez ce script."
}

Write-Host ''
Write-Host "Preparation de la machine - role $Role" -ForegroundColor Cyan
Write-Host "  nom cible        : $Nom"
Write-Host "  adresse cible    : $Ip/$Prefixe"
Write-Host "  passerelle / DNS : $Passerelle / $Dns"

# ---------------------------------------------------------------------------
Etape '1/5 Reseau'
# ---------------------------------------------------------------------------
# On prend la premiere carte physique active : dans une VM il n'y en a qu'une.
$carte = Get-NetAdapter -Physical | Where-Object Status -eq 'Up' | Select-Object -First 1

if (-not $carte) {
    throw "Aucune carte reseau active. Verifiez que la VM est bien connectee au reseau virtuel."
}

Info "Carte utilisee : $($carte.Name)"

$adresseActuelle = Get-NetIPAddress -InterfaceIndex $carte.ifIndex -AddressFamily IPv4 -ErrorAction SilentlyContinue |
    Where-Object { $_.IPAddress -notlike '169.254.*' } |
    Select-Object -First 1

if ($adresseActuelle -and $adresseActuelle.IPAddress -eq $Ip -and $adresseActuelle.PrefixOrigin -eq 'Manual') {
    Ok "Adresse $Ip deja configuree en statique."
} else {
    if ($adresseActuelle) {
        Info "Remplacement de l'adresse actuelle ($($adresseActuelle.IPAddress), $($adresseActuelle.PrefixOrigin))..."
        Remove-NetIPAddress -InterfaceIndex $carte.ifIndex -AddressFamily IPv4 -Confirm:$false -ErrorAction SilentlyContinue
        Remove-NetRoute -InterfaceIndex $carte.ifIndex -AddressFamily IPv4 -Confirm:$false -ErrorAction SilentlyContinue
    }

    New-NetIPAddress -InterfaceIndex $carte.ifIndex -IPAddress $Ip -PrefixLength $Prefixe -DefaultGateway $Passerelle | Out-Null
    Ok "Adresse fixe $Ip/$Prefixe configuree."
}

Set-DnsClientServerAddress -InterfaceIndex $carte.ifIndex -ServerAddresses $Dns
Ok "DNS : $Dns"

# Profil reseau prive : indispensable pour que le pare-feu laisse passer le
# trafic entre les deux VM (un profil « Public » bloque tout par defaut).
try {
    Set-NetConnectionProfile -InterfaceIndex $carte.ifIndex -NetworkCategory Private -ErrorAction Stop
    Ok 'Profil reseau : Prive.'
} catch {
    Info "Profil reseau inchange ($($_.Exception.Message))"
}

# Test de connectivite Internet : les etapes suivantes telechargent.
$internet = Test-NetConnection -ComputerName 'github.com' -Port 443 -InformationLevel Quiet -WarningAction SilentlyContinue

if ($internet) {
    Ok 'Acces Internet disponible.'
} else {
    Write-Warning "Pas d'acces Internet. Docker et Git ne pourront pas etre telecharges."
    Write-Warning "Verifiez que la carte de la VM est en mode NAT ou Pont, puis relancez."
}

# ---------------------------------------------------------------------------
Etape '2/5 Nom de la machine'
# ---------------------------------------------------------------------------
if ($env:COMPUTERNAME -eq $Nom) {
    Ok "La machine s'appelle deja $Nom."
} else {
    Rename-Computer -NewName $Nom -Force
    $redemarrageRequis = $true
    Ok "Machine renommee en $Nom (effectif apres redemarrage)."
}

# ---------------------------------------------------------------------------
Etape '3/5 Git'
# ---------------------------------------------------------------------------
if (Get-Command git -ErrorAction SilentlyContinue) {
    Ok "Git deja installe : $(git --version)"
} elseif (-not $internet) {
    Info 'Ignore : pas d acces Internet.'
} else {
    Info 'Telechargement de Git pour Windows...'

    $publication = Invoke-RestMethod -Uri 'https://api.github.com/repos/git-for-windows/git/releases/latest' `
        -Headers @{ 'User-Agent' = 'setup-script' }

    $paquet = $publication.assets |
        Where-Object { $_.name -like '*64-bit.exe' -and $_.name -notlike '*rc*' } |
        Select-Object -First 1

    if (-not $paquet) {
        Write-Warning "Installateur Git introuvable. Installez-le manuellement depuis https://git-scm.com/download/win"
    } else {
        $installateur = Join-Path $env:TEMP $paquet.name
        Invoke-WebRequest -Uri $paquet.browser_download_url -OutFile $installateur -UseBasicParsing

        Info 'Installation silencieuse...'
        Start-Process -FilePath $installateur `
            -ArgumentList '/VERYSILENT', '/NORESTART', '/NOCANCEL', '/SP-', '/CLOSEAPPLICATIONS' -Wait

        Remove-Item $installateur -Force -ErrorAction SilentlyContinue

        # Rend git utilisable immediatement, sans rouvrir la console.
        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine') + ';' +
                    [Environment]::GetEnvironmentVariable('Path', 'User')

        if (Get-Command git -ErrorAction SilentlyContinue) {
            Ok "Git installe : $(git --version)"
        } else {
            Ok 'Git installe (rouvrez la console pour utiliser la commande git).'
        }
    }
}

# ---------------------------------------------------------------------------
Etape '4/5 Docker'
# ---------------------------------------------------------------------------
if ($Role -eq 'Client') {
    Ok 'Ignore : la machine cliente n a besoin que d un navigateur.'
} else {
    $roleContainers = Get-WindowsFeature -Name Containers -ErrorAction SilentlyContinue

    if ($roleContainers -and -not $roleContainers.Installed) {
        Info 'Installation du role Containers...'
        $resultat = Install-WindowsFeature -Name Containers

        if ($resultat.RestartNeeded -eq 'Yes') { $redemarrageRequis = $true }

        Ok 'Role Containers installe.'
    } else {
        Ok 'Role Containers deja present.'
    }

    if (Get-Command docker -ErrorAction SilentlyContinue) {
        Ok "Docker deja installe : $(docker --version)"
    } elseif ($redemarrageRequis) {
        Info 'Docker sera installe apres le redemarrage : relancez ce script.'
    } elseif (-not $internet) {
        Info 'Ignore : pas d acces Internet.'
    } else {
        Info 'Installation de Docker CE (script officiel Microsoft, plusieurs minutes)...'

        $installateur = Join-Path $env:TEMP 'install-docker-ce.ps1'
        Invoke-WebRequest -UseBasicParsing `
            -Uri 'https://raw.githubusercontent.com/microsoft/Windows-Containers/Main/helpful_tools/Install-DockerCE/install-docker-ce.ps1' `
            -OutFile $installateur

        & $installateur

        $env:Path = [Environment]::GetEnvironmentVariable('Path', 'Machine')
        $redemarrageRequis = $true

        Ok 'Docker installe.'
    }

    if ((Get-Service docker -ErrorAction SilentlyContinue) -and (Get-Service docker).Status -ne 'Running') {
        Start-Service docker
        Set-Service -Name docker -StartupType Automatic
        Ok 'Service Docker demarre et automatique.'
    }

    # --- Docker Compose v2 : greffon de la ligne de commande ----------------
    # Il n'est pas toujours fourni avec le moteur sur Windows : on le depose
    # dans le dossier des greffons, ce qui active « docker compose ».
    $dossierGreffons = Join-Path $env:ProgramData 'Docker\cli-plugins'
    $compose = Join-Path $dossierGreffons 'docker-compose.exe'

    if (Test-Path $compose) {
        Ok 'Docker Compose deja present.'
    } elseif (-not $internet) {
        Info 'Ignore : pas d acces Internet.'
    } else {
        Info 'Telechargement de Docker Compose...'

        New-Item -ItemType Directory -Path $dossierGreffons -Force | Out-Null
        Invoke-WebRequest -UseBasicParsing `
            -Uri 'https://github.com/docker/compose/releases/latest/download/docker-compose-windows-x86_64.exe' `
            -OutFile $compose

        Ok 'Docker Compose installe.'
    }
}

# ---------------------------------------------------------------------------
Etape '5/5 Verification'
# ---------------------------------------------------------------------------
Write-Host ("    {0,-18} {1}" -f 'Nom actuel', $env:COMPUTERNAME)
Write-Host ("    {0,-18} {1}" -f 'Adresse IPv4', ((Get-NetIPAddress -InterfaceIndex $carte.ifIndex -AddressFamily IPv4).IPAddress -join ', '))

foreach ($commande in @('git', 'docker')) {
    if ($Role -eq 'Client' -and $commande -eq 'docker') { continue }

    $version = if (Get-Command $commande -ErrorAction SilentlyContinue) {
        (& $commande --version) 2>$null
    } else {
        'non installe'
    }

    Write-Host ("    {0,-18} {1}" -f $commande, $version)
}

if ($Role -eq 'Serveur' -and (Get-Command docker -ErrorAction SilentlyContinue)) {
    $versionCompose = try { (docker compose version) 2>$null } catch { 'non disponible' }
    Write-Host ("    {0,-18} {1}" -f 'docker compose', $versionCompose)
}

Write-Host ''

if ($redemarrageRequis) {
    Write-Warning 'Un redemarrage est necessaire.'
    Write-Host '    Redemarrez, puis RELANCEZ ce script pour terminer :' -ForegroundColor Yellow
    Write-Host '        Restart-Computer' -ForegroundColor Yellow
} elseif ($Role -eq 'Serveur') {
    Write-Host 'Machine prete.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Etapes suivantes :' -ForegroundColor Cyan
    Write-Host '    git clone <url-du-depot> C:\microservices\app'
    Write-Host '    cd C:\microservices\app'
    Write-Host '    .\deploy\1-setup-serveur.ps1'
    Write-Host '    Copy-Item .env.example .env   puis renseigner les deux secrets'
    Write-Host '    .\deploy\2-deployer.ps1'
} else {
    Write-Host 'Machine cliente prete.' -ForegroundColor Green
    Write-Host ''
    Write-Host 'Etape suivante : pointer le DNS vers le serveur une fois celui-ci deploye :' -ForegroundColor Cyan
    Write-Host "    Set-DnsClientServerAddress -InterfaceIndex $($carte.ifIndex) -ServerAddresses 192.168.10.10"
}

Write-Host ''

#Requires -Version 5.1
<#
.SYNOPSIS
    Deploiement de la plateforme sur SRV-MICRO-01 : mise a jour du code, reconstruction
    des images et verification de la sante.

.DESCRIPTION
    1. git fetch / checkout / pull --ff-only sur la branche de deploiement,
    2. verification de la presence du fichier .env (secrets),
    3. docker compose up -d --build (les services dependent les uns des autres,
       l'ordre est gere par depends_on + condition service_healthy),
    4. attente de l'etat « Healthy » de la gateway agregee (/health/services).

    Code de sortie 0 si la plateforme est saine, 1 sinon.

.EXAMPLE
    .\deploy.ps1
.EXAMPLE
    .\deploy.ps1 -Branche main -DelaiSecondes 300
#>
[CmdletBinding()]
param(
    [string]$Racine = 'C:\microservices\app',
    [string]$Branche = 'claude/happy-hamilton-wkmhpe',
    [string]$BaseUrl = 'http://localhost',
    [int]$DelaiSecondes = 240
)

$ErrorActionPreference = 'Stop'

function Etape([string]$Texte) { Write-Host "`n==> $Texte" -ForegroundColor Cyan }

Set-Location $Racine

Etape "Mise a jour du code (branche $Branche)"
git fetch origin $Branche
if ($LASTEXITCODE -ne 0) { throw 'git fetch a echoue (reseau ou identifiants).' }
git checkout $Branche
if ($LASTEXITCODE -ne 0) { throw "Branche $Branche introuvable localement ou distante." }
git pull --ff-only origin $Branche
if ($LASTEXITCODE -ne 0) { throw 'git pull --ff-only a echoue : des modifications locales divergent.' }
Write-Host "Commit deploye : $(git rev-parse --short HEAD)"

Etape 'Verification de la configuration'
if (-not (Test-Path '.env')) {
    throw "Fichier .env absent. Copiez .env.example en .env et renseignez les secrets (JWT_KEY, INTERNAL_API_KEY, CERT_PASSWORD)."
}
if (-not (Test-Path 'C:\microservices\certs\gateway.pfx')) {
    throw 'Certificat manquant : lancez infra\06-generate-cert.ps1.'
}

Etape 'Construction et demarrage (docker compose)'
docker compose --env-file .env up -d --build
if ($LASTEXITCODE -ne 0) { throw 'docker compose a echoue : consultez « docker compose logs ».' }

Etape "Attente de la sante de la plateforme (max $DelaiSecondes s)"
$fin = (Get-Date).AddSeconds($DelaiSecondes)
$sante = $null
do {
    Start-Sleep -Seconds 5
    try {
        $sante = Invoke-RestMethod -Uri "$BaseUrl/health/services" -TimeoutSec 10
    } catch {
        if ($_.ErrorDetails.Message) { $sante = $_.ErrorDetails.Message | ConvertFrom-Json }
    }
    $pret = $sante -and $sante.status -eq 'Healthy'
    Write-Host ("  {0} service(s) joignable(s) sur {1}" -f $(if ($sante) { $sante.healthy } else { 0 }), $(if ($sante) { $sante.total } else { 7 }))
} until ($pret -or (Get-Date) -gt $fin)

if ($pret) {
    Write-Host "`nPlateforme saine : $BaseUrl (interface web) et $BaseUrl/swagger (API)." -ForegroundColor Green
    docker compose ps
    exit 0
}

Write-Host "`nPlateforme NON saine apres $DelaiSecondes s." -ForegroundColor Red
docker compose ps
exit 1

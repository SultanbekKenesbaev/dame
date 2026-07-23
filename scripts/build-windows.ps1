[CmdletBinding()]
param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [string]$CertificateThumbprint,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$artifacts = Join-Path $root 'artifacts\windows'

dotnet test (Join-Path $root 'src\DailyGate.Api.Tests\DailyGate.Api.Tests.csproj') -c $Configuration
dotnet publish (Join-Path $root 'src\DailyGate.Windows.Service\DailyGate.Windows.Service.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $artifacts 'service')
dotnet publish (Join-Path $root 'src\DailyGate.Windows.Client\DailyGate.Windows.Client.csproj') -c $Configuration -r win-x64 --self-contained true -o (Join-Path $artifacts 'client')

function Sign-Artifact([string]$Path) {
    if (-not $CertificateThumbprint) { return }
    $signtool = (Get-Command signtool.exe -ErrorAction Stop).Source
    & $signtool sign /sha1 $CertificateThumbprint /fd SHA256 /tr $TimestampUrl /td SHA256 $Path
    if ($LASTEXITCODE -ne 0) { throw "Signing failed: $Path" }
    & $signtool verify /pa /v $Path
    if ($LASTEXITCODE -ne 0) { throw "Signature verification failed: $Path" }
}

Sign-Artifact (Join-Path $artifacts 'service\DailyGate.Service.exe')
Sign-Artifact (Join-Path $artifacts 'client\DailyGate.Client.exe')
$servicePublish = Join-Path $artifacts 'service'
$clientPublish = Join-Path $artifacts 'client'
dotnet build (Join-Path $root 'installer\DailyGate.Installer.wixproj') -c $Configuration "-p:ServicePublishDir=$servicePublish" "-p:ClientPublishDir=$clientPublish"

$msi = Join-Path $root "installer\bin\$Configuration\DailyGate.Setup.msi"
if (-not (Test-Path $msi)) { throw "MSI was not created: $msi" }
Sign-Artifact $msi
if (-not $CertificateThumbprint) {
    Write-Warning 'Artifacts are unsigned. Production rollout requires -CertificateThumbprint.'
}

Write-Host "Windows artifacts created in $artifacts" -ForegroundColor Green

[CmdletBinding()]
param(
    [string]$RepositoryArchiveUrl = 'https://github.com/SultanbekKenesbaev/dame/archive/refs/heads/main.zip',
    [string]$CertificateThumbprint
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

if (-not $IsWindows -and $PSVersionTable.PSEdition -eq 'Core') {
    throw 'This script must be run on Windows 10 or Windows 11.'
}

$desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
if ([string]::IsNullOrWhiteSpace($desktop)) {
    throw 'Windows Desktop folder was not found.'
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$bundle = Join-Path $desktop "DailyGate-Build-$stamp"
$downloadDirectory = Join-Path $bundle 'download'
$sourceDirectory = Join-Path $bundle 'source'
$releaseDirectory = Join-Path $bundle 'release'
$toolsDirectory = Join-Path $bundle 'tools'
$archive = Join-Path $downloadDirectory 'dailygate.zip'
$transcript = Join-Path $bundle 'build.log'

New-Item -ItemType Directory -Path $downloadDirectory, $sourceDirectory, $releaseDirectory, $toolsDirectory -Force | Out-Null
Start-Transcript -Path $transcript -Force | Out-Null

try {
    Write-Host '1/5 Downloading the current DailyGate source code...' -ForegroundColor Cyan
    Invoke-WebRequest -Uri $RepositoryArchiveUrl -OutFile $archive -UseBasicParsing
    Expand-Archive -Path $archive -DestinationPath $sourceDirectory -Force

    $projectRoot = Get-ChildItem -Path $sourceDirectory -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'scripts\build-windows.ps1') } |
        Select-Object -First 1
    if ($null -eq $projectRoot) {
        throw 'The downloaded archive does not contain the DailyGate project.'
    }

    Write-Host '2/5 Checking .NET 10 SDK...' -ForegroundColor Cyan
    $dotnetCommand = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    $hasDotnet10 = $false
    if ($null -ne $dotnetCommand) {
        $dotnetPath = $dotnetCommand.Source
        $hasDotnet10 = @(& $dotnetPath --list-sdks) -match '^10\.'
    }

    if (-not $hasDotnet10) {
        Write-Host 'Downloading a private .NET 10 SDK for this build...' -ForegroundColor Yellow
        $dotnetInstall = Join-Path $toolsDirectory 'dotnet-install.ps1'
        $dotnetDirectory = Join-Path $toolsDirectory 'dotnet'
        Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $dotnetInstall -UseBasicParsing
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $dotnetInstall -Channel '10.0' -InstallDir $dotnetDirectory -NoPath
        if ($LASTEXITCODE -ne 0) {
            throw ".NET 10 SDK installation failed with exit code $LASTEXITCODE."
        }
        $env:Path = "$dotnetDirectory;$env:Path"
        $dotnetCommand = Get-Command dotnet.exe -ErrorAction Stop
    }

    Write-Host '3/5 Building the Windows service, client and MSI...' -ForegroundColor Cyan
    $buildScript = Join-Path $projectRoot.FullName 'scripts\build-windows.ps1'
    $buildArguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $buildScript, '-Configuration', 'Release')
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $buildArguments += @('-CertificateThumbprint', $CertificateThumbprint)
    }
    & powershell.exe @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "DailyGate build failed with exit code $LASTEXITCODE. See $transcript"
    }

    Write-Host '4/5 Saving the release bundle...' -ForegroundColor Cyan
    $msi = Join-Path $projectRoot.FullName 'installer\bin\Release\DailyGate.Setup.msi'
    $client = Join-Path $projectRoot.FullName 'artifacts\windows\client'
    $service = Join-Path $projectRoot.FullName 'artifacts\windows\service'
    if (-not (Test-Path $msi)) { throw "MSI was not created: $msi" }
    if (-not (Test-Path $client)) { throw "Client artifacts were not created: $client" }
    if (-not (Test-Path $service)) { throw "Service artifacts were not created: $service" }

    $savedMsi = Join-Path $releaseDirectory 'DailyGate.Setup.msi'
    Copy-Item -Path $msi -Destination $savedMsi -Force
    Copy-Item -Path $client -Destination (Join-Path $releaseDirectory 'client') -Recurse -Force
    Copy-Item -Path $service -Destination (Join-Path $releaseDirectory 'service') -Recurse -Force

    $hash = Get-FileHash -Path $savedMsi -Algorithm SHA256
    "DailyGate.Setup.msi  $($hash.Hash)" | Set-Content -Path (Join-Path $releaseDirectory 'SHA256.txt') -Encoding ASCII

    Write-Host '5/5 Build completed.' -ForegroundColor Green
    Write-Host "MSI: $savedMsi" -ForegroundColor Green
    Write-Host "Complete bundle: $bundle" -ForegroundColor Green
    if ([string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        Write-Warning 'This is an unsigned test build. A trusted code-signing certificate is required for production rollout.'
    }

    Stop-Transcript | Out-Null
    Start-Process explorer.exe -ArgumentList "/select,`"$savedMsi`""
}
catch {
    Write-Error $_
    try { Stop-Transcript | Out-Null } catch { }
    Write-Host "The downloaded source and build log were kept in: $bundle" -ForegroundColor Yellow
    exit 1
}

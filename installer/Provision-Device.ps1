[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^https://')][string]$ApiUrl,
    [Parameter(Mandatory = $true)][string]$EnrollmentCode,
    [string]$DeviceName = $env:COMPUTERNAME
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

$edition = (Get-ComputerInfo -Property WindowsProductName).WindowsProductName
if ($edition -notmatch 'Enterprise|Education|IoT') {
    throw "Shell Launcher requires Windows Enterprise, Education or IoT Enterprise. Current edition: $edition"
}

$serviceExe = Join-Path $env:ProgramFiles 'DailyGate\Service\DailyGate.Service.exe'
$kioskScript = Join-Path $env:ProgramFiles 'DailyGate\Tools\Configure-Kiosk.ps1'
if (-not (Test-Path $serviceExe)) { throw 'DailyGate MSI must be installed first.' }

Stop-Service -Name DailyGateService -ErrorAction SilentlyContinue
& $serviceExe enroll --api-url $ApiUrl.TrimEnd('/') --code $EnrollmentCode --name $DeviceName
if ($LASTEXITCODE -ne 0) { throw "Device enrollment failed with exit code $LASTEXITCODE." }
Start-Service -Name DailyGateService

$taskName = 'DailyGate Configure Kiosk'
$action = New-ScheduledTaskAction -Execute 'PowerShell.exe' -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$kioskScript`""
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(5)
$principalSystem = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principalSystem -Force | Out-Null
Start-ScheduledTask -TaskName $taskName

$deadline = (Get-Date).AddMinutes(2)
do {
    Start-Sleep -Seconds 2
    $state = (Get-ScheduledTask -TaskName $taskName).State
} while ($state -eq 'Running' -and (Get-Date) -lt $deadline)
$result = (Get-ScheduledTaskInfo -TaskName $taskName).LastTaskResult
Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
if ($result -ne 0) { throw "Kiosk configuration task failed with code $result." }

Write-Host 'DailyGate was enrolled and kiosk mode was configured. Restart Windows to verify the installation.' -ForegroundColor Green

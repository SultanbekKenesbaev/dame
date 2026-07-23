[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidatePattern('^https://')][string]$ApiUrl,
    [Parameter(Mandatory = $true)][string]$EnrollmentCode,
    [string]$DeviceName = $env:COMPUTERNAME,
    [string]$DemoUser,
    [switch]$DemoMode
)

$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Run this script from an elevated PowerShell session.'
}

$edition = (Get-ComputerInfo -Property WindowsProductName).WindowsProductName
$supportsKiosk = $edition -match 'Enterprise|Education|IoT'
$useDemoMode = $DemoMode -or -not $supportsKiosk
if ($useDemoMode) {
    Write-Warning "DailyGate demo mode is being configured for $edition. It does not provide a secure kiosk boundary."
}

$serviceExe = Join-Path $env:ProgramFiles 'DailyGate\Service\DailyGate.Service.exe'
$clientExe = Join-Path $env:ProgramFiles 'DailyGate\Client\DailyGate.Client.exe'
$kioskScript = Join-Path $env:ProgramFiles 'DailyGate\Tools\Configure-Kiosk.ps1'
if (-not (Test-Path $serviceExe)) { throw 'DailyGate MSI must be installed first.' }
if (-not (Test-Path $clientExe)) { throw 'DailyGate client is missing. Repair or reinstall the MSI.' }

Stop-Service -Name DailyGateService -ErrorAction SilentlyContinue
$enrollArguments = @('enroll', '--api-url', $ApiUrl.TrimEnd('/'), '--code', $EnrollmentCode, '--name', $DeviceName)
if ($useDemoMode) { $enrollArguments += '--demo-mode' }
& $serviceExe @enrollArguments
if ($LASTEXITCODE -ne 0) { throw "Device enrollment failed with exit code $LASTEXITCODE." }
Set-Service -Name DailyGateService -StartupType Automatic
Start-Service -Name DailyGateService

$demoTaskName = 'DailyGate Demo Client'
Unregister-ScheduledTask -TaskName $demoTaskName -Confirm:$false -ErrorAction SilentlyContinue
if ($useDemoMode) {
    $currentUser = $DemoUser
    if ([string]::IsNullOrWhiteSpace($currentUser)) {
        $currentUser = (Get-CimInstance Win32_ComputerSystem).UserName
    }
    if ([string]::IsNullOrWhiteSpace($currentUser)) {
        $currentUser = $identity.Name
    }
    $demoAction = New-ScheduledTaskAction -Execute $clientExe -Argument '--demo'
    $demoTrigger = New-ScheduledTaskTrigger -AtLogOn -User $currentUser
    $demoPrincipal = New-ScheduledTaskPrincipal -UserId $currentUser -LogonType Interactive -RunLevel Limited
    Register-ScheduledTask -TaskName $demoTaskName -Action $demoAction -Trigger $demoTrigger -Principal $demoPrincipal -Force | Out-Null
    Write-Host "DailyGate was enrolled in demo mode for $currentUser." -ForegroundColor Green
    Write-Host 'Sign out and sign in again to start the client. Use the safe exit button if the service is unavailable.' -ForegroundColor Yellow
    return
}

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

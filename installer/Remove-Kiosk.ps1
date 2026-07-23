[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]$identity
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run as administrator.' }

$edition = (Get-ComputerInfo -Property WindowsProductName).WindowsProductName
if ($edition -match 'Enterprise|Education|IoT') {
    $taskName = 'DailyGate Remove Kiosk'
    $command = @'
$instance = Get-CimInstance -Namespace "root\cimv2\mdm\dmmap" -ClassName "MDM_AssignedAccess"
$instance.ShellLauncher = $null
Set-CimInstance -CimInstance $instance | Out-Null
'@
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    $action = New-ScheduledTaskAction -Execute 'PowerShell.exe' -Argument "-NoProfile -EncodedCommand $encoded"
    $trigger = New-ScheduledTaskTrigger -Once -At (Get-Date).AddSeconds(3)
    $system = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $system -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    Start-Sleep -Seconds 8
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
Unregister-ScheduledTask -TaskName 'DailyGate Demo Client' -Confirm:$false -ErrorAction SilentlyContinue
Unregister-ScheduledTask -TaskName 'DailyGate Desktop Client' -Confirm:$false -ErrorAction SilentlyContinue
Stop-Service DailyGateService -ErrorAction SilentlyContinue
Write-Host 'DailyGate startup configuration removed. Restart Windows, then uninstall DailyGate.' -ForegroundColor Yellow

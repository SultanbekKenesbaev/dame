[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
if ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value -ne 'S-1-5-18') {
    throw 'Configure-Kiosk.ps1 must run as LocalSystem. Use Provision-Device.ps1.'
}

$edition = (Get-ComputerInfo -Property WindowsProductName).WindowsProductName
if ($edition -notmatch 'Enterprise|Education|IoT') { throw "Unsupported Windows edition: $edition" }
Enable-WindowsOptionalFeature -Online -FeatureName Client-EmbeddedShellLauncher -All -NoRestart | Out-Null

# Policies are placed in the default profile before Windows creates the managed auto-logon kiosk account.
$defaultHive = 'HKU\DailyGateDefault'
reg.exe load $defaultHive 'C:\Users\Default\NTUSER.DAT' | Out-Null
try {
    $systemPolicy = 'Registry::HKEY_USERS\DailyGateDefault\Software\Microsoft\Windows\CurrentVersion\Policies\System'
    $explorerPolicy = 'Registry::HKEY_USERS\DailyGateDefault\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer'
    New-Item $systemPolicy -Force | Out-Null
    New-Item $explorerPolicy -Force | Out-Null
    New-ItemProperty $systemPolicy -Name DisableTaskMgr -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $explorerPolicy -Name NoRun -Value 1 -PropertyType DWord -Force | Out-Null
    New-ItemProperty $explorerPolicy -Name NoControlPanel -Value 1 -PropertyType DWord -Force | Out-Null
} finally { [gc]::Collect(); reg.exe unload $defaultHive | Out-Null }

$profileId = '{6E12D4B9-54D2-4D93-899F-3BB320960AC4}'
$client = "$env:ProgramFiles\DailyGate\Client\DailyGate.Client.exe"
$xml = @"
<ShellLauncherConfiguration xmlns="http://schemas.microsoft.com/ShellLauncher/2018/Configuration" xmlns:V2="http://schemas.microsoft.com/ShellLauncher/2019/Configuration">
  <Profiles>
    <DefaultProfile><Shell Shell="%SystemRoot%\explorer.exe" /></DefaultProfile>
    <Profile Id="$profileId">
      <Shell Shell="$client" V2:AppType="Desktop" V2:AllAppsFullScreen="true">
        <ReturnCodeActions><ReturnCodeAction ReturnCode="0" Action="RestartShell" /></ReturnCodeActions>
        <DefaultAction Action="RestartShell" />
      </Shell>
    </Profile>
  </Profiles>
  <Configs><Config><AutoLogonAccount /><Profile Id="$profileId" /></Config></Configs>
</ShellLauncherConfiguration>
"@

$encoded = [System.Net.WebUtility]::HtmlEncode($xml)
$instance = Get-CimInstance -Namespace 'root\cimv2\mdm\dmmap' -ClassName 'MDM_AssignedAccess'
$instance.ShellLauncher = $encoded
Set-CimInstance -CimInstance $instance | Out-Null
exit 0

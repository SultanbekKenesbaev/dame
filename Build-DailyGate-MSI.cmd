@echo off
setlocal
title DailyGate MSI Builder

set "DAILYGATE_BOOTSTRAP=%TEMP%\Get-DailyGate-Msi.ps1"
echo Downloading the DailyGate build script...
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ProgressPreference='SilentlyContinue'; [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing -Uri 'https://raw.githubusercontent.com/SultanbekKenesbaev/dame/main/scripts/Get-DailyGate-Msi.ps1' -OutFile '%DAILYGATE_BOOTSTRAP%'"
if errorlevel 1 goto failed

powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%DAILYGATE_BOOTSTRAP%"
if errorlevel 1 goto failed

echo.
echo DailyGate MSI was built successfully.
pause
exit /b 0

:failed
echo.
echo DailyGate build failed. Read the error above and the build.log file on the Desktop.
pause
exit /b 1

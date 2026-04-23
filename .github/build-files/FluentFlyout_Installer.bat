@echo off
TITLE FluentFlyout Installer
CLS

SET AppNamePattern=*FluentFlyout*

ECHO ===================================================
ECHO   Installing FluentFlyout...
ECHO   Please follow the prompts in the blue window.
ECHO ===================================================
ECHO.

cd /d "%~dp0SystemFiles"

:: Run the VS generated script
PowerShell -NoProfile -ExecutionPolicy Bypass -Command "& './Add-AppDevPackage.ps1' -SkipLoggingTelemetry"

IF %ERRORLEVEL% NEQ 0 (
    ECHO.
    ECHO Something went wrong during installation.
    PAUSE
    EXIT
)

ECHO.
ECHO Installation complete. Launching App...

:: This PowerShell command finds the Package Family Name of the app we just installed
:: and launches it using the "shell:AppsFolder" protocol.
PowerShell -Command "& { $pkg = Get-AppxPackage '%AppNamePattern%' | Select-Object -First 1; if ($pkg) { $exe = Join-Path $pkg.InstallLocation 'FluentFlyout\FluentFlyout.exe'; Start-Process $exe } else { Write-Host 'Could not find installed App.' } }"

:: Close this installer window automatically
EXIT
@echo off
TITLE FluentFlyout Installer
CLS

SET AppNamePattern=*FluentFlyout*

ECHO ===================================================
ECHO   Installing FluentFlyout...
ECHO   Please follow the prompts in the blue window.
ECHO ===================================================
ECHO.

:: Prerequisite: the GitHub build is framework-dependent and needs the
:: .NET Desktop Runtime 10 (x64). Without it the package installs fine but
:: the app silently fails to start (no window, no Task Manager entry).
SET "DotNetFxKey=HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App"
reg query "%DotNetFxKey%" 2>NUL | findstr /R "10\." >NUL
IF %ERRORLEVEL% NEQ 0 (
    ECHO ---------------------------------------------------
    ECHO   .NET 10 Desktop Runtime ^(x64^) was NOT detected.
    ECHO   FluentFlyout will NOT start without it.
    ECHO   Download it here:
    ECHO   https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe
    ECHO ---------------------------------------------------
    ECHO.
    SET /P OpenDotNet="Open the download page now? (Y/N): "
    IF /I "%OpenDotNet%"=="Y" (
        START "" "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe"
        ECHO.
        ECHO Install the runtime, then run this installer again.
        PAUSE
        EXIT /B 1
    ) ELSE (
        ECHO Continuing without the runtime - the app may fail to start.
        ECHO.
    )
)

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
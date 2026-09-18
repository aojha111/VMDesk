@echo off
:: install.cmd - VMDesk portable extraction and setup
setlocal

:: Determine installation directory
if "%~1" neq "" set INSTALL_DIR=%1%
if "%INSTALL_DIR%" == "" set INSTALL_DIR=%APPDATA%\..\Local\VMDesk

:: Create data directories
mkdir "%APPDATA%\..\Local\VMDesk" 2>nul
mkdir "%APPDATA%\..\Local\VMDesk\Logs" 2>nul
mkdir "%APPDATA%\..\Local\VMDesk\Backups" 2>nul
mkdir "%APPDATA%\..\Local\VMDesk\Exports" 2>nul

:: Extract portable zip
if exist "VMDesk-Portable-x64.zip" (
    echo Extracting VMDesk portable distribution...
    powershell -Command "Expand-Archive -Path 'VMDesk-Portable-x64.zip' -DestinationPath '%INSTALL_DIR%' -Force"
    echo Extraction complete.
)

:: Run the application once to initialize database and settings
echo Initializing VMDesk...
"%INSTALL_DIR%\VMDesk.exe"

:: Set theme based on system
echo VMDesk setup complete.
echo Data located at: %APPDATA%\..\Local\VMDesk\vmdesk.db

endlocal
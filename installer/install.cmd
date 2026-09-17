@echo off
setlocal
set "INSTALL_DIR=%LOCALAPPDATA%\VMDesk"
if not exist "%INSTALL_DIR%" mkdir "%INSTALL_DIR%"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "Expand-Archive -LiteralPath '%~dp0VMDesk-win-x64.zip' -DestinationPath '%LOCALAPPDATA%\VMDesk' -Force"
set "START_MENU=%APPDATA%\Microsoft\Windows\Start Menu\Programs"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut((Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs\VMDesk.lnk')); $shortcut.TargetPath = Join-Path $env:LOCALAPPDATA 'VMDesk\VMDesk.exe'; $shortcut.WorkingDirectory = Join-Path $env:LOCALAPPDATA 'VMDesk'; $shortcut.Save()"
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$shell = New-Object -ComObject WScript.Shell; $shortcut = $shell.CreateShortcut((Join-Path $env:USERPROFILE 'Desktop\VMDesk.lnk')); $shortcut.TargetPath = Join-Path $env:LOCALAPPDATA 'VMDesk\VMDesk.exe'; $shortcut.WorkingDirectory = Join-Path $env:LOCALAPPDATA 'VMDesk'; $shortcut.Save()"
start "" "%INSTALL_DIR%\VMDesk.exe"
exit /b 0
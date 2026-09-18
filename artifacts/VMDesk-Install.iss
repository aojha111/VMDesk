; Inno Setup script for VMDesk application
; Generated from artifacts rebuild

[Setup]
; Basic installer information
AppName=VMDesk
AppVersion=1.0
AppPublisher=Abhijit Ojha
AppPublisherURL=https://github.com/aojha111/VMDesk
; Use doesntresolveconfigfiles on Windows 10/11
PrivilegeLevel=low
; Create uninstaller
Uninstallable=true
; Default installation path
DefaultDirName={pf}\VMDesk
; Output file name
OutputBaseFileName=VMDesk-Setup.exe
; Compression
SolidCompression=yes
; Allow restart if needed
RestartIfNeeded=true
; Icons
DefaultGroupName=VMDesk
UninstallDisplayIcon={app}\VMDesk.exe
; Setup icons
SetupIconFile=resources\app.ico
; Tasks
; disable start menu for portable-like install
; Compression level
Compression=lzma

[Tasks]
name: "desktopicon"; description: "{cm:CreateDesktopIcon}"; groupdescription: "{cm:AddProgramItem}"
name: "startmenu"; description: "{cm:CreateStartMenuDir}"; groupdescription: "{cm:AddProgramGroup}"

[Files]
; Copy the self-contained executable
Source: "..\..\..\src\VMDesk.App\bin\Release\net10.0-windows\win-x64\VMDesk.exe"; DestDir: "{app}"; Flags: ignoreversion
; Copy theme resources
Source: "..\..\..\src\VMDesk.App\Resources\Themes\*.xaml"; DestDir: "{app}\Resources\Themes"; Flags: ignoreversion recursesubdirs
Source: "..\..\..\src\VMDesk.App\Resources\App.xaml"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\..\..\src\VMDesk.App\Resources\app.ico"; DestDir: "{app}"; Flags: ignoreversion
; Copy portable zip for extraction
Source: "..\..\..\artifacts\VMDesk-Portable-x64.zip"; DestDir: "{tmp}"; Flags: ignoreversion
; Copy startup script
Source: "..\..\..\artifacts\install.cmd"; DestDir: "{tmp}"; Flags: ignoreversion

[Run]
; Extract the portable zip
Filename: "{tmp}\install.cmd"; Parameters: "/extract"; Flags: waituntilterminated
; Create shortcuts
Filename: "{app}\VMDesk.exe"; Flags: ignoreversion
CreateDirectoryIcon={group}\VMDesk.exe
CreateStartMenuDir={group}
CreateDesktopIcon={task desktopicn}

[UninstallRun]
; Remove installed files
Delete "{app}\VMDesk.exe"
Delete "{app}\Resources\Themes"
Delete "{app}\App.xaml"
Delete "{app}\app.ico"

[UninstallDelete]
; Remove uninstaller
Delete "{app}"

; vim: set sw=4 ts=4 expandtab:
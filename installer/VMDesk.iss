#define AppName "VMDesk"
#define AppVersion "1.0.1"
#define AppPublisher "VMDesk"
#define SourceDir GetEnv("SourceDir")

[Setup]
AppId={{A7B9D1A3-3EBE-4B5E-8E15-000000000001}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\VMDesk
DefaultGroupName=VMDesk
OutputDir=..\publish_output\installer
OutputBaseFilename=VMDesk-Setup-{#AppVersion}
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VMDesk"; Filename: "{app}\VMDesk.exe"
Name: "{autodesktop}\VMDesk"; Filename: "{app}\VMDesk.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\VMDesk.exe"; Description: "Launch VMDesk"; Flags: nowait postinstall skipifsilent
#ifndef PublishDir
#define PublishDir "..\artifacts\win-x64"
#endif
#define AppVersion "1.0.0"

[Setup]
AppId={{A7B3E1C4-6D20-4F8A-9C55-1E2D4A6B8C90}
AppName=Defuse
AppVersion={#AppVersion}
AppPublisher=Defuse
DefaultDirName={autopf}\Defuse
DefaultGroupName=Defuse
OutputDir=..\artifacts
OutputBaseFilename=Defuse-windows-x64-setup
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Defuse.exe
SetupIconFile=..\src\Desktop\Assets\defuse.ico

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Defuse"; Filename: "{app}\Defuse.exe"
Name: "{autodesktop}\Defuse"; Filename: "{app}\Defuse.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Defuse.exe"; Description: "Launch Defuse"; Flags: nowait postinstall

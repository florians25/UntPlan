; Inno-Setup-Skript für UntPlan (Aufruf: ISCC /DQuelle=<Publish-Ordner> UntPlan.iss)
#ifndef Quelle
  #define Quelle "..\artifacts\UntPlan"
#endif
#define AppName "UntPlan"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6E5B2C1A-6A0E-4F7B-9C55-2B7E0D4A9F11}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=UntPlan
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayIcon={app}\UntPlan.exe
OutputBaseFilename=UntPlan-Setup-{#AppVersion}
SetupIconFile=..\build\untplan.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequiredOverridesAllowed=dialog
ChangesAssociations=yes

[Languages]
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#Quelle}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\UntPlan.exe"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\UntPlan.exe"; Tasks: desktopicon

[Registry]
Root: HKA; Subkey: "Software\Classes\.up"; ValueType: string; ValueName: ""; ValueData: "UntPlan.Schuldatei"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.untplan"; ValueType: string; ValueName: ""; ValueData: "UntPlan.Schuldatei"; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\UntPlan.Schuldatei"; ValueType: string; ValueName: ""; ValueData: "UntPlan Schuldatei"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\UntPlan.Schuldatei\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\UntPlan.exe,0"
Root: HKA; Subkey: "Software\Classes\UntPlan.Schuldatei\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\UntPlan.exe"" ""%1"""

[Run]
Filename: "{app}\UntPlan.exe"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

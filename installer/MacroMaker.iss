#define MyAppName "MacroMaker"
#define MyAppVersion "2.0.0"
#define MyAppPublisher "MacroMaker"
#define MyAppExeName "MacroMaker.exe"

[Setup]
AppId={{0BFFB163-E5FA-47D2-AAC0-E1E5DA6A2B2F}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion={#MyAppVersion}
VersionInfoProductName={#MyAppName}
VersionInfoDescription=Visual macro recorder and automation builder
DefaultDirName={localappdata}\Programs\MacroMaker
DefaultGroupName=MacroMaker
DisableProgramGroupPage=yes
UninstallDisplayName=MacroMaker
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=..\dist
OutputBaseFilename=MacroMaker-Setup
SetupIconFile=..\Assets\MacroMaker.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
SetupLogging=yes
CloseApplications=yes
RestartApplications=no

[Files]
Source: "..\dist\MacroMaker.exe"; DestDir: "{app}"; Flags: ignoreversion

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Icons]
Name: "{group}\MacroMaker"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall MacroMaker"; Filename: "{uninstallexe}"
Name: "{autodesktop}\MacroMaker"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch MacroMaker"; Flags: nowait postinstall skipifsilent

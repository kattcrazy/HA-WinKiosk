#define MyAppName "HA WinKiosk"
#define MyAppPublisher "HA WinKiosk"
#define MyAppURL "https://github.com/kattcrazy/HA-WinKiosk"
#define MyAppExeName "HAWinKiosk.exe"
#define MyAppVersion "3.21.8"

[Setup]
AppId={{7E91D8B9-4E5A-4B6B-B3A6-4F89B7A5E2F2}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
; Per-user install (no admin / UAC). Matches self-contained publish output from tools\Build-ExeFromGDrive.ps1.
DefaultDirName={localappdata}\Programs\HA WinKiosk
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesInstallIn64BitMode=x64
ArchitecturesAllowed=x64compatible
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
OutputDir=.\output
OutputBaseFilename=HAWinKiosk-Setup
SetupIconFile=..\light_logo.ico

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "..\src\HAWinKiosk\bin\Release\net8.0-windows\win-x64\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall

[UninstallDelete]
Type: filesandordirs; Name: "{userappdata}\HA-WinKiosk"

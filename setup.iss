#define MyAppName "ClipTyper"
#define MyAppVersion "1.5.1"
#define MyAppPublisher "unpaved028"
#define MyAppExeName "ClipTyper.exe"

[Setup]
; Unique AppId for version upgrades
AppId={{8B8EE0F4-FE28-44A7-8E3B-2A0CE67BCBEF}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={userpf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
; Uninstall icon
UninstallDisplayIcon={app}\{#MyAppExeName}
OutputDir=.
OutputBaseFilename=ClipTyper-Setup
Compression=lzma
SolidCompression=yes
; Runs entirely in user space (no admin rights required)
PrivilegesRequired=lowest
DisableWelcomePage=yes
DisableDirPage=yes
; Mutex check to detect running ClipTyper process & close applications automatically
AppMutex=Local\ClipTyper_SingleInstance_Mutex,ClipTyper_SingleInstance_Mutex
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "publish-winget\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{userprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
; Configure autostart for the current user
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue

[Run]
; Interactive install: show checkbox to launch the app
Filename: "{app}\{#MyAppExeName}"; Flags: nowait postinstall; Description: "Launch ClipTyper"; Check: IsNotSilent
; Silent install (e.g. via Winget): launch automatically in the background
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: IsSilent

[Code]
function IsSilent: Boolean;
begin
  Result := WizardSilent;
end;

function IsNotSilent: Boolean;
begin
  Result := not WizardSilent;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  // Force terminate any remaining ClipTyper process before installation to avoid locked file errors
  Exec('taskkill.exe', '/f /im ClipTyper.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Result := '';
end;

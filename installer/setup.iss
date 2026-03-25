; WG-Autoconnect Inno Setup Script
; Builds a professional installer with EULA, desktop shortcut option, and UAC elevation.

#define MyAppName "WG-Autoconnect"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Artixskillz"
#define MyAppURL "https://github.com/Artixskillz/WG-Autoconnect"
#define MyAppExeName "WG-Autoconnect.exe"

[Setup]
AppId={{E4A7C3F1-2B8D-4E5F-9A1C-6D3E8F0B2A4C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir=..\installer\output
OutputBaseFilename=WG-Autoconnect-Setup
SetupIconFile=..\app\app.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
VersionInfoDescription={#MyAppName} Installer
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked
Name: "startup"; Description: "Start automatically with Windows (via Task Scheduler, no UAC prompt)"; GroupDescription: "Startup:"

[Files]
Source: "..\app\bin\Release\net8.0-windows\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch after install — the app's own "Run at Startup" handles Task Scheduler registration
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[UninstallRun]
; Remove startup task on uninstall
Filename: "schtasks"; Parameters: "/delete /tn ""WG-Autoconnect"" /f"; Flags: runhidden; RunOnceId: "RemoveStartupTask"
; Run the app's own uninstaller to clean up %AppData% data
Filename: "{app}\{#MyAppExeName}"; Parameters: "--uninstall-silent"; Flags: runhidden; RunOnceId: "CleanAppData"

[Code]
var
  ResultCode: Integer;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Kill any running instance before installing
  if CurStep = ssInstall then
  begin
    Exec('taskkill', '/F /IM WG-Autoconnect.exe', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;

  // After install: register startup via the app's own XML-based task registration
  // (handles paths with spaces, scoped to current user, includes logon delay)
  if CurStep = ssPostInstall then
  begin
    if WizardIsTaskSelected('startup') then
    begin
      Exec(ExpandConstant('{app}\{#MyAppExeName}'), '--register-startup', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    end;
  end;
end;

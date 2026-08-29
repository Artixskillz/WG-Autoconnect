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
Source: "..\app\bin\Release\net8.0-windows10.0.17763.0\win-x64\publish\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Launch after install — the app's own "Run at Startup" handles Task Scheduler registration
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent runascurrentuser

[Code]
var
  ResultCode: Integer;

// Encode up-to-3-part version strings comparably (1.2.3 -> 1002003)
function VersionToInt(ver: String): Int64;
var
  i, idx: Integer;
  parts: array[0..2] of Integer;
  ch: Char;
begin
  for i := 0 to 2 do parts[i] := 0;
  idx := 0;
  for i := 1 to Length(ver) do
  begin
    ch := ver[i];
    if (ch >= '0') and (ch <= '9') then
    begin
      if idx <= 2 then parts[idx] := parts[idx] * 10 + Ord(ch) - Ord('0');
    end
    else if ch = '.' then
    begin
      idx := idx + 1;
      if idx > 2 then break;
    end;
  end;
  Result := Int64(parts[0]) * 1000000 + parts[1] * 1000 + parts[2];
end;

function InstalledVersion(): String;
var
  v: String;
begin
  Result := '';
  if RegQueryStringValue(HKLM64,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E4A7C3F1-2B8D-4E5F-9A1C-6D3E8F0B2A4C}_is1',
      'DisplayVersion', v) then
    Result := v
  else if RegQueryStringValue(HKLM32,
      'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\{E4A7C3F1-2B8D-4E5F-9A1C-6D3E8F0B2A4C}_is1',
      'DisplayVersion', v) then
    Result := v;
end;

// Downgrade guard: warn (default No) when installing an older version over a
// newer one. Same or newer versions upgrade in place silently, settings kept.
function InitializeSetup(): Boolean;
var
  installed: String;
begin
  Result := True;
  installed := InstalledVersion();
  if (installed <> '') and (VersionToInt(installed) > VersionToInt('{#MyAppVersion}')) then
    Result := MsgBox('A newer version of WG-Autoconnect (' + installed + ') is already installed.' #13#10 #13#10
      + 'Are you sure you want to install the older version {#MyAppVersion} over it?',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Params: String;
begin
  // Runs before files are removed, so {app}\exe is still available.
  if CurUninstallStep = usUninstall then
  begin
    // Stop the running app first — otherwise it keeps automating (and could
    // reconnect the tunnel) while we're uninstalling underneath it
    Exec('taskkill', '/F /IM {#MyAppExeName}', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // The app's own silent uninstaller tears down the tunnel if this app
    // connected it (ownership marker) and removes the startup task.
    // Ask whether to also delete settings/logs — keeping them means a future
    // reinstall resumes the user's configuration. Silent uninstalls keep the
    // historical full-cleanup behavior.
    Params := '--uninstall-silent';
    if not UninstallSilent then
    begin
      if MsgBox('Also delete your WG-Autoconnect settings and logs?' #13#10 #13#10
                'Choose No to keep them — if you reinstall later, your configuration will be picked up automatically.',
                mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDNO then
        Params := '--uninstall-silent --keep-settings';
    end;
    Exec(ExpandConstant('{app}\{#MyAppExeName}'), Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);

    // Belt-and-braces: ensure the startup task is gone even if the step above failed
    Exec('schtasks', '/delete /tn "WG-Autoconnect" /f', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

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

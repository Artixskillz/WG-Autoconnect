#Requires AutoHotkey v2.0
#SingleInstance Force

; ==============================================================================
; AUTO-ELEVATION (Runs script as Admin automatically)
; ==============================================================================
if !A_IsAdmin
{
    try Run '*RunAs "' A_ScriptFullPath '"'
    catch MsgBox "This script requires Administrator privileges to manage the WireGuard service."
    ExitApp
}
Persistent

; ==============================================================================
; CONFIGURATION & STATE
; Class properties are globally accessible without 'global' declarations.
; ==============================================================================
class Config
{
    static IniFile       := ""   ; Set after class definition
    static WG_Config     := ""
    static WG_Exe        := ""
    static TunnelName    := ""
    static TunnelService := ""
    static TargetApps    := []
    static PollInterval  := 5000
    static GracePeriodMs := 10000
    static DisconnectOnExit := true
    static StartupTask   := "WG-Autoconnect"
}

class State
{
    static IsPaused          := false
    static IsTransitioning   := false
    static DisconnectPending := false
    static ConnectRetries    := 0
    static DisconnectRetries := 0
}

Config.IniFile := A_ScriptDir "\WG-Autoconnect.ini"

; ==============================================================================
; LOAD EXTERNAL CONFIGURATION
; On first run, creates a default .ini and opens it for the user to fill in.
; ==============================================================================
if !FileExist(Config.IniFile)
{
    CreateDefaultIni(Config.IniFile)
    Run "notepad.exe " Config.IniFile
    MsgBox "Welcome to WG-Autoconnect!`n`n"
        . "A configuration file has been created at:`n" Config.IniFile
        . "`n`nFill in your settings and restart the script.",
        "WG-Autoconnect: First Run", 64
    ExitApp
}

Config.WG_Config        := IniRead(Config.IniFile, "WireGuard",  "ConfigPath",          "")
Config.WG_Exe           := IniRead(Config.IniFile, "WireGuard",  "ExePath",             "C:\Program Files\WireGuard\wireguard.exe")
Config.DisconnectOnExit := IniRead(Config.IniFile, "Monitoring", "DisconnectOnExit",    "1") == "1"
AppsRaw                 := IniRead(Config.IniFile, "Monitoring", "Apps",                "")
try Config.PollInterval  := Integer(IniRead(Config.IniFile, "Monitoring", "PollInterval",       "5000"))
try Config.GracePeriodMs := Integer(IniRead(Config.IniFile, "Monitoring", "GracePeriodSeconds", "10")) * 1000

for app in StrSplit(AppsRaw, ",")
{
    trimmed := Trim(app)
    if trimmed != ""
        Config.TargetApps.Push(trimmed)
}

; ==============================================================================
; VALIDATE CONFIGURATION
; ==============================================================================
errors := ""
if Config.WG_Config == "" || Config.WG_Config == "C:\Path\To\Your\vpn_config.conf"
    errors .= "• WireGuard config path not set.`n  Edit ConfigPath= in WG-Autoconnect.ini`n`n"
else if !FileExist(Config.WG_Config)
    errors .= "• WireGuard config file not found:`n  " Config.WG_Config "`n`n"
if !FileExist(Config.WG_Exe)
    errors .= "• WireGuard executable not found:`n  " Config.WG_Exe "`n`n"
if Config.TargetApps.Length == 0
    errors .= "• No apps configured.`n  Edit Apps= in WG-Autoconnect.ini`n`n"

if errors != ""
{
    result := MsgBox("Please fix the following and restart:`n`n" errors
        . "Would you like to open the configuration file now?",
        "WG-Autoconnect: Config Error", 52)  ; 52 = Yes/No + Warning icon
    if result == "Yes"
        Run "notepad.exe " Config.IniFile
    ExitApp
}

; Auto-derive tunnel name from config filename — no manual entry needed
SplitPath Config.WG_Config, , , , &derivedName
Config.TunnelName    := derivedName
Config.TunnelService := "WireGuardTunnel$" derivedName

; ==============================================================================
; TRAY MENU
; ==============================================================================
IsRegisteredAtStartup := (RunWait(A_ComSpec ' /c schtasks /query /tn "' Config.StartupTask '"', , "Hide") == 0)
StartupLabel := IsRegisteredAtStartup ? "Disable Run at Startup" : "Run at Windows Startup"

A_TrayMenu.Delete()
A_TrayMenu.Add("WG-Autoconnect", (*) => {})
A_TrayMenu.Disable("WG-Autoconnect")
A_TrayMenu.Add("Monitoring: Active | VPN: Checking...", (*) => {})
A_TrayMenu.Disable("Monitoring: Active | VPN: Checking...")
A_TrayMenu.Add()
A_TrayMenu.Add("Pause Monitoring", TogglePause)
A_TrayMenu.Add()
A_TrayMenu.Add("Force Connect", ForceConnect)
A_TrayMenu.Add("Force Disconnect", ForceDisconnect)
A_TrayMenu.Add()
A_TrayMenu.Add(StartupLabel, ToggleStartup)
A_TrayMenu.Add()
A_TrayMenu.Add("Edit Configuration", EditConfig)
A_TrayMenu.Add("View Log", ViewLog)
A_TrayMenu.Add("Reload", (*) => Reload())
A_TrayMenu.Add("Exit", ExitScript)

; ==============================================================================
; START
; ==============================================================================
OnExit ExitHandler
LogEvent("Started | Tunnel: " Config.TunnelName " | Watching: " JoinList(Config.TargetApps))
CheckAndToggleVPN()                          ; Immediate check — no 5-second wait
SetTimer CheckAndToggleVPN, Config.PollInterval

; ==============================================================================
; CORE LOGIC
; ==============================================================================

CheckAndToggleVPN()
{
    if State.IsPaused || State.IsTransitioning
        return

    AppsRunning := AreAnyAppsRunning(Config.TargetApps)
    VpnUp       := IsServiceRunning(Config.TunnelService)
    UpdateTrayStatus(VpnUp)

    if AppsRunning
    {
        ; Cancel any pending grace-period disconnect if an app came back
        if State.DisconnectPending
        {
            SetTimer DisconnectAfterGrace, 0
            State.DisconnectPending := false
            LogEvent("Grace-period disconnect cancelled — app came back.")
        }
        if !VpnUp
            ConnectVPN()
    }
    else
    {
        ; Schedule disconnect after grace period to handle brief app restarts
        if VpnUp && !State.DisconnectPending
        {
            State.DisconnectPending := true
            SetTimer DisconnectAfterGrace, -Config.GracePeriodMs
            LogEvent("Apps closed. Disconnecting in " (Config.GracePeriodMs // 1000) "s (grace period).")
        }
    }
}

DisconnectAfterGrace()
{
    State.DisconnectPending := false
    if !AreAnyAppsRunning(Config.TargetApps) && IsServiceRunning(Config.TunnelService) && !State.IsTransitioning
        DisconnectVPN()
}

ConnectVPN()
{
    State.IsTransitioning := true
    try
    {
        Run '"' Config.WG_Exe '" /installtunnelservice "' Config.WG_Config '"', , "Hide"
        TrayTip "Connecting to " Config.TunnelName "...", "WG-Autoconnect", 1
        SetTimer () => TrayTip(), -3000
        LogEvent("Connecting | Tunnel: " Config.TunnelName)
    }
    catch as e
    {
        TrayTip "Failed to connect: " e.Message, "WG-Autoconnect", 3
        LogEvent("ERROR: Connect failed | " e.Message)
        State.IsTransitioning := false
        return
    }
    SetTimer VerifyConnect, -5000   ; Confirm success after 5s, retry once if not
}

DisconnectVPN()
{
    State.IsTransitioning := true
    try
    {
        Run '"' Config.WG_Exe '" /uninstalltunnelservice "' Config.TunnelName '"', , "Hide"
        TrayTip "Disconnecting from " Config.TunnelName "...", "WG-Autoconnect", 1
        SetTimer () => TrayTip(), -3000
        LogEvent("Disconnecting | Tunnel: " Config.TunnelName)
    }
    catch as e
    {
        TrayTip "Failed to disconnect: " e.Message, "WG-Autoconnect", 3
        LogEvent("ERROR: Disconnect failed | " e.Message)
        State.IsTransitioning := false
        return
    }
    SetTimer VerifyDisconnect, -5000   ; Confirm success after 5s, retry once if not
}

; Verify connect succeeded; retry once if not, then surface an error
VerifyConnect()
{
    if IsServiceRunning(Config.TunnelService)
    {
        State.ConnectRetries  := 0
        State.IsTransitioning := false
        UpdateTrayStatus(true)
        LogEvent("Connection verified | Tunnel: " Config.TunnelName)
    }
    else if State.ConnectRetries < 1
    {
        State.ConnectRetries++
        LogEvent("Connect not confirmed, retrying (attempt " State.ConnectRetries ")...")
        try Run '"' Config.WG_Exe '" /installtunnelservice "' Config.WG_Config '"', , "Hide"
        SetTimer VerifyConnect, -5000
    }
    else
    {
        State.ConnectRetries  := 0
        State.IsTransitioning := false
        TrayTip "VPN failed to connect after retry.", "WG-Autoconnect", 3
        LogEvent("ERROR: VPN failed to connect after retry.")
    }
}

; Verify disconnect succeeded; retry once if not, then surface an error
VerifyDisconnect()
{
    if !IsServiceRunning(Config.TunnelService)
    {
        State.DisconnectRetries := 0
        State.IsTransitioning   := false
        UpdateTrayStatus(false)
        LogEvent("Disconnect verified | Tunnel: " Config.TunnelName)
    }
    else if State.DisconnectRetries < 1
    {
        State.DisconnectRetries++
        LogEvent("Disconnect not confirmed, retrying (attempt " State.DisconnectRetries ")...")
        try Run '"' Config.WG_Exe '" /uninstalltunnelservice "' Config.TunnelName '"', , "Hide"
        SetTimer VerifyDisconnect, -5000
    }
    else
    {
        State.DisconnectRetries := 0
        State.IsTransitioning   := false
        TrayTip "VPN failed to disconnect after retry.", "WG-Autoconnect", 3
        LogEvent("ERROR: VPN failed to disconnect after retry.")
    }
}

AreAnyAppsRunning(AppList)
{
    for AppName in AppList
        if ProcessExist(AppName)
            return true
    return false
}

IsServiceRunning(ServiceName)
{
    try
        return (RunWait(A_ComSpec ' /c sc query "' ServiceName '" | find "RUNNING"', , "Hide") == 0)
    catch
        return false
}

UpdateTrayStatus(VpnUp)
{
    static lastLabel := "Monitoring: Active | VPN: Checking..."

    vpnText  := VpnUp ? "VPN: Connected" : "VPN: Disconnected"
    monText  := State.IsPaused ? "Monitoring: Paused" : "Monitoring: Active"
    newLabel := monText " | " vpnText

    if newLabel != lastLabel
    {
        try A_TrayMenu.Rename(lastLabel, newLabel)
        catch
            try A_TrayMenu.Rename("Monitoring: Active | VPN: Checking...", newLabel)
        lastLabel := newLabel
    }

    ; Show watched apps in tooltip (Windows caps tooltip at 127 chars)
    A_IconTip := SubStr("WG-Autoconnect`n" newLabel "`nWatching: " JoinList(Config.TargetApps), 1, 127)
}

; Append a timestamped entry; rotate log to .old when it exceeds 512 KB
LogEvent(Message)
{
    LogFile := A_ScriptDir "\WG-Autoconnect.log"
    if FileExist(LogFile) && FileGetSize(LogFile) > 524288
    {
        try FileMove LogFile, LogFile ".old", 1
        FileAppend "--- Log rotated ---`n", LogFile
    }
    FileAppend FormatTime(, "yyyy-MM-dd HH:mm:ss") "  " Message "`n", LogFile
}

JoinList(Items)
{
    result := ""
    for item in Items
        result .= (result ? ", " : "") item
    return result
}

CreateDefaultIni(Path)
{
    content := "[WireGuard]`n"
        . "; Full path to your WireGuard tunnel configuration file (.conf)`n"
        . "ConfigPath=C:\Path\To\Your\vpn_config.conf`n"
        . "`n"
        . "; Path to WireGuard executable — only change if installed elsewhere`n"
        . "ExePath=C:\Program Files\WireGuard\wireguard.exe`n"
        . "`n"
        . "[Monitoring]`n"
        . "; Comma-separated list of executable names to monitor`n"
        . "; VPN connects when ANY are running, disconnects when ALL are closed`n"
        . "Apps=YourApp1.exe,YourApp2.exe`n"
        . "`n"
        . "; How often to check running processes in milliseconds (default: 5000 = 5s)`n"
        . "PollInterval=5000`n"
        . "`n"
        . "; Seconds to wait after all apps close before disconnecting (default: 10)`n"
        . "; Prevents unnecessary churn if an app briefly restarts`n"
        . "GracePeriodSeconds=10`n"
        . "`n"
        . "; Disconnect VPN when this script exits: 1=yes, 0=no`n"
        . "DisconnectOnExit=1`n"
    FileAppend content, Path
}

; ==============================================================================
; TRAY MENU HANDLERS
; ==============================================================================

TogglePause(*)
{
    State.IsPaused := !State.IsPaused
    if State.IsPaused
    {
        ; Cancel any pending grace-period disconnect
        if State.DisconnectPending
        {
            SetTimer DisconnectAfterGrace, 0
            State.DisconnectPending := false
        }
        A_TrayMenu.Rename("Pause Monitoring", "Resume Monitoring")
        UpdateTrayStatus(IsServiceRunning(Config.TunnelService))
        TrayTip "Monitoring paused — VPN will not change automatically.", "WG-Autoconnect", 1
        LogEvent("Monitoring paused by user.")
    }
    else
    {
        A_TrayMenu.Rename("Resume Monitoring", "Pause Monitoring")
        UpdateTrayStatus(IsServiceRunning(Config.TunnelService))
        TrayTip "Monitoring resumed.", "WG-Autoconnect", 1
        LogEvent("Monitoring resumed by user.")
        CheckAndToggleVPN()
    }
}

ForceConnect(*)
{
    if IsServiceRunning(Config.TunnelService)
    {
        TrayTip "VPN is already connected.", "WG-Autoconnect", 1
        return
    }
    try
    {
        Run '"' Config.WG_Exe '" /installtunnelservice "' Config.WG_Config '"', , "Hide"
        TrayTip "Force-connecting to " Config.TunnelName "...", "WG-Autoconnect", 1
        LogEvent("Force-connect by user.")
    }
    catch as e
    {
        TrayTip "Force connect failed: " e.Message, "WG-Autoconnect", 3
        LogEvent("ERROR: Force-connect failed | " e.Message)
    }
}

ForceDisconnect(*)
{
    if !IsServiceRunning(Config.TunnelService)
    {
        TrayTip "VPN is already disconnected.", "WG-Autoconnect", 1
        return
    }
    try
    {
        Run '"' Config.WG_Exe '" /uninstalltunnelservice "' Config.TunnelName '"', , "Hide"
        TrayTip "Force-disconnecting from " Config.TunnelName "...", "WG-Autoconnect", 1
        LogEvent("Force-disconnect by user.")
    }
    catch as e
    {
        TrayTip "Force disconnect failed: " e.Message, "WG-Autoconnect", 3
        LogEvent("ERROR: Force-disconnect failed | " e.Message)
    }
}

; Toggle Windows startup registration via Task Scheduler (runs elevated, no UAC prompt)
ToggleStartup(*)
{
    taskExists := (RunWait(A_ComSpec ' /c schtasks /query /tn "' Config.StartupTask '"', , "Hide") == 0)
    if taskExists
    {
        exitCode := RunWait(A_ComSpec ' /c schtasks /delete /tn "' Config.StartupTask '" /f', , "Hide")
        if exitCode != 0
        {
            TrayTip "Failed to remove startup task. Check permissions.", "WG-Autoconnect", 3
            LogEvent("ERROR: Failed to remove startup task (exit code " exitCode ").")
            return
        }
        A_TrayMenu.Rename("Disable Run at Startup", "Run at Windows Startup")
        TrayTip "Removed from Windows startup.", "WG-Autoconnect", 1
        LogEvent("Removed from Windows startup.")
    }
    else
    {
        cmd := 'schtasks /create /tn "' Config.StartupTask '"'
            . ' /tr "\"' A_AhkPath '\" \"' A_ScriptFullPath '\""'
            . ' /sc ONLOGON /rl HIGHEST /f'
        exitCode := RunWait(A_ComSpec ' /c ' cmd, , "Hide")
        if exitCode != 0
        {
            TrayTip "Failed to register startup task. Check permissions.", "WG-Autoconnect", 3
            LogEvent("ERROR: Failed to register startup task (exit code " exitCode ").")
            return
        }
        A_TrayMenu.Rename("Run at Windows Startup", "Disable Run at Startup")
        TrayTip "Added to Windows startup (elevated, no UAC prompt).", "WG-Autoconnect", 1
        LogEvent("Added to Windows startup via Task Scheduler.")
    }
}

EditConfig(*)
{
    Run "notepad.exe " Config.IniFile
}

ViewLog(*)
{
    LogFile := A_ScriptDir "\WG-Autoconnect.log"
    if FileExist(LogFile)
        Run "notepad.exe " LogFile
    else
        TrayTip "No log file yet — it will appear after the first event.", "WG-Autoconnect", 1
}

ExitScript(*)
{
    ExitApp
}

ExitHandler(ExitReason, ExitCode)
{
    LogEvent("Exiting | Reason: " ExitReason)
    if Config.DisconnectOnExit && IsServiceRunning(Config.TunnelService)
    {
        LogEvent("Disconnecting VPN on exit...")
        try RunWait '"' Config.WG_Exe '" /uninstalltunnelservice "' Config.TunnelName '"', , "Hide"
    }
}

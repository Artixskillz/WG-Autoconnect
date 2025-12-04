#Requires AutoHotkey v2.0
#SingleInstance Force

; ==============================================================================
; AUTO-ELEVATION (Runs script as Admin automatically)
; ==============================================================================
if !A_IsAdmin
{
    try
    {
        Run '*RunAs "' A_ScriptFullPath '"'
    }
    catch
    {
        MsgBox "This script requires Administrator privileges to manage the WireGuard service."
    }
    ExitApp
}
Persistent

; ==============================================================================
; USER CONFIGURATION - PLEASE EDIT THIS SECTION
; ==============================================================================

; 1. Path to your WireGuard configuration file (.conf)
;    Example: "C:\Users\YourName\Documents\WireGuard\my_tunnel.conf"
WG_Config := "C:\Path\To\Your\vpn_config.conf"

; 2. Name of your WireGuard tunnel (usually the filename without .conf)
;    Example: "my_tunnel"
TunnelName := "vpn_config"

; 3. List of applications to monitor (Executable names)
;    The VPN will activate when ANY of these are open, and disconnect when ALL are closed.
;    Example: ["outlook.exe", "chrome.exe", "steam.exe"]
TargetApps := ["YourApp1.exe", "YourApp2.exe"]

; 4. Path to WireGuard executable (usually default, change only if installed elsewhere)
WG_Exe := "C:\Program Files\WireGuard\wireguard.exe"

; ==============================================================================
; END OF CONFIGURATION
; ==============================================================================

; STARTUP CHECK: Verify Config Exists
if !FileExist(WG_Config)
{
    MsgBox "ERROR: WireGuard Configuration File Not Found!`n`nPath: " WG_Config "`n`nPlease edit the script and check the 'WG_Config' path.", "WG-Autoconnect Error", 16
    ExitApp
}

TunnelServiceName := "WireGuardTunnel$" TunnelName
IsPaused := false

; TRAY MENU SETUP
A_TrayMenu.Delete() ; Clear default items
A_TrayMenu.Add("Status: Active", (*) => {}) ; Read-only status
A_TrayMenu.Disable("Status: Active")
A_TrayMenu.Add() ; Separator
A_TrayMenu.Add("Pause Monitoring", TogglePause)
A_TrayMenu.Add() ; Separator
A_TrayMenu.Add("Force Connect", ForceConnect)
A_TrayMenu.Add("Force Disconnect", ForceDisconnect)
A_TrayMenu.Add() ; Separator
A_TrayMenu.Add("Edit Configuration", EditScript)
A_TrayMenu.Add("Exit", ExitScript)
A_IconTip := "WG-Autoconnect: Active"

; Check every 5 seconds
SetTimer CheckAndToggleVPN, 5000

CheckAndToggleVPN() {
    global WG_Config, WG_Exe, TunnelServiceName, TargetApps, TunnelName, IsPaused
    
    if (IsPaused)
        return

    AppsRunning := AreAnyAppsRunning(TargetApps)
    
    if (AppsRunning) {
        ; Check if VPN is already running
        if (!IsServiceRunning(TunnelServiceName)) {
            Run '"' WG_Exe '" /installtunnelservice "' WG_Config '"', , "Hide"
            TrayTip "Activating WireGuard VPN (" TunnelName ")", "VPN Automation"
            SetTimer () => TrayTip(), -3000
        }
    } else {
        if (IsServiceRunning(TunnelServiceName)) {
            Run '"' WG_Exe '" /uninstalltunnelservice "' TunnelName '"', , "Hide"
            TrayTip "Deactivating WireGuard VPN", "VPN Automation"
            SetTimer () => TrayTip(), -3000
        }
    }
}

AreAnyAppsRunning(AppList) {
    for AppName in AppList {
        if (ProcessExist(AppName)) {
            return true
        }
    }
    return false
}

IsServiceRunning(ServiceName) {
    try {
        ExitCode := RunWait(A_ComSpec ' /c sc query "' ServiceName '" | find "RUNNING"', , "Hide")
        return (ExitCode == 0)
    } catch {
        return false
    }
}

; TRAY MENU FUNCTIONS

TogglePause(*) {
    global IsPaused
    IsPaused := !IsPaused
    if (IsPaused) {
        A_TrayMenu.Rename("Pause Monitoring", "Resume Monitoring")
        A_TrayMenu.Rename("Status: Active", "Status: Paused")
        A_IconTip := "WG-Autoconnect: Paused"
        TrayTip "Monitoring Paused", "VPN Automation"
    } else {
        A_TrayMenu.Rename("Resume Monitoring", "Pause Monitoring")
        A_TrayMenu.Rename("Status: Paused", "Status: Active")
        A_IconTip := "WG-Autoconnect: Active"
        TrayTip "Monitoring Resumed", "VPN Automation"
        CheckAndToggleVPN() ; Run check immediately
    }
}

ForceConnect(*) {
    global WG_Config, WG_Exe, TunnelServiceName
    Run '"' WG_Exe '" /installtunnelservice "' WG_Config '"', , "Hide"
    TrayTip "Forcing Connection...", "VPN Automation"
}

ForceDisconnect(*) {
    global WG_Exe, TunnelName
    Run '"' WG_Exe '" /uninstalltunnelservice "' TunnelName '"', , "Hide"
    TrayTip "Forcing Disconnection...", "VPN Automation"
}

EditScript(*) {
    Edit
}

ExitScript(*) {
    ExitApp
}

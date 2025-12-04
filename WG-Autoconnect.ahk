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

TunnelServiceName := "WireGuardTunnel$" TunnelName

; Check every 5 seconds
SetTimer CheckAndToggleVPN, 5000

CheckAndToggleVPN() {
    global WG_Config, WG_Exe, TunnelServiceName, TargetApps, TunnelName
    
    AppsRunning := AreAnyAppsRunning(TargetApps)
    
    if (AppsRunning) {
        ; Check if VPN is already running
        if (!IsServiceRunning(TunnelServiceName)) {
            Run '"' WG_Exe '" /installtunnelservice "' WG_Config '"', , "Hide"
            TrayTip "Activating WireGuard VPN (" TunnelName ")", "VPN Automation"
            SetTimer () => TrayTip(), -3000 ; Hide tray tip after 3 seconds
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
    ; Check service state using sc query
    ; RunWait returns the exit code
    try {
        ExitCode := RunWait(A_ComSpec ' /c sc query "' ServiceName '" | find "RUNNING"', , "Hide")
        return (ExitCode == 0)
    } catch {
        return false
    }
}

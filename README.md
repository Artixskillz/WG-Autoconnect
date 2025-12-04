# WG-Autoconnect

**WG-Autoconnect** is a lightweight AutoHotkey script for Windows that automatically manages your WireGuard VPN tunnel based on the applications you are using.

## 🚀 Features

*   **App-Based Automation**: Automatically activates your WireGuard tunnel when you open specific target applications (e.g., Outlook, eM Client) and deactivates it when they are closed.
*   **Fully Configurable**: Easily customize the list of target applications, tunnel name, and configuration paths.
*   **Auto-Elevation**: Automatically handles Administrator privileges required for service management.
*   **Resource Efficient**: Keeps your VPN connection off when not in use.

## 📋 Requirements

*   Windows 10/11
*   [AutoHotkey v2.0+](https://www.autohotkey.com/)
*   [WireGuard for Windows](https://www.wireguard.com/install/)

## ⚙️ Configuration

1.  Download `WG-Autoconnect.ahk`.
2.  Open the file in a text editor.
3.  Edit the **Configuration** section at the top:
    ```autohotkey
    WG_Config := "C:\Path\To\Your\vpn_config.conf"
    TunnelName := "vpn_config"
    TargetApps := ["outlook.exe", "MailClient.exe"]
    ```

## 🏃 Usage

Double-click `WG-Autoconnect.ahk` to start the script. It will run in the system tray and monitor your target applications.

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

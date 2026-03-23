# WG-Autoconnect

**Automatically connect and disconnect your WireGuard VPN based on the apps you're running.**

WG-Autoconnect is a lightweight Windows tray application that monitors your running processes and manages your WireGuard tunnel accordingly — VPN connects when a monitored app opens, and disconnects when they all close.

![Settings Screenshot](https://img.shields.io/badge/platform-Windows%2010%2F11-blue) ![License](https://img.shields.io/badge/license-MIT-green) ![.NET](https://img.shields.io/badge/.NET-8.0-purple)

---

## Features

- **App-based VPN automation** — VPN connects when any monitored app starts, disconnects when all close
- **Grace period** — Configurable delay before disconnecting, so brief app restarts don't churn the tunnel
- **Connection verification** — Confirms connect/disconnect succeeded, with automatic retry on failure
- **Live status** — Real-time VPN state, running apps, and connection status in the settings panel and tray tooltip
- **Process picker** — Browse and select from currently running processes instead of typing names manually
- **Auto-detection** — Automatically finds your WireGuard installation and `.conf` files
- **System tray icon** — Color-coded icon shows VPN state at a glance (red = connected, gray = disconnected, orange = transitioning, yellow = paused)
- **Manual override** — Force Connect / Force Disconnect from the tray menu without interfering with automation
- **Startup registration** — One-click "Run at Windows Startup" via Task Scheduler (elevated, no UAC prompt on login)
- **Config file watcher** — External changes to `settings.json` are picked up automatically
- **Log rotation** — Timestamped log file, auto-rotates at 512 KB, viewable from the tray menu
- **Auto-update check** — Notifies you on startup if a newer release is available (also available from tray menu)
- **Uninstaller** — Clean removal from tray menu or `--uninstall` flag (removes startup task, settings, logs)
- **Single-file exe** — Self-contained, no installer needed, no runtime dependencies

## Download

Grab the latest `WG-Autoconnect.exe` from the [Releases](https://github.com/Artixskillz/WG-Autoconnect/releases) page.

> **Requirements:** Windows 10/11 (x64) and [WireGuard for Windows](https://www.wireguard.com/install/).
> No .NET runtime install needed — everything is bundled in the exe.

## Quick Start

1. **Download** `WG-Autoconnect.exe` from [Releases](https://github.com/Artixskillz/WG-Autoconnect/releases)
2. **Run it** — accept the UAC prompt (admin is required to control WireGuard tunnel services)
3. **Configure** — the setup form opens on first run:
   - Select your WireGuard `.conf` file (auto-discovered from Desktop/Downloads/Documents)
   - WireGuard executable is auto-detected
   - Add apps to monitor — type a name or click **Pick...** to select from running processes
   - Set your poll interval and grace period
4. **Save** — you'll be asked if you want to start with Windows
5. **Done** — the app minimizes to the tray and starts monitoring

## Tray Menu

| Item | Description |
|---|---|
| Status line | Live monitoring and VPN state |
| Pause / Resume | Suspend automation without exiting |
| Force Connect | Connect immediately, regardless of running apps |
| Force Disconnect | Disconnect immediately — suppresses auto-reconnect until an app triggers it again |
| Run at Startup | Toggle Task Scheduler registration (elevated, no UAC on login) |
| Settings | Open the settings panel with live VPN status |
| View Log | Open `app.log` in Notepad |
| Check for Updates | Check GitHub for a newer release |
| Uninstall | Remove startup task, settings, logs, and optionally the exe |
| Exit | Exit the app (disconnects VPN if "Disconnect on exit" is enabled) |

## How It Works

Every few seconds (configurable), WG-Autoconnect checks if any of your monitored processes are running:

- **App detected + VPN down** → connects the tunnel via `wireguard.exe /installtunnelservice`
- **No apps + VPN up** → starts the grace period, then disconnects via `/uninstalltunnelservice`
- **Manual VPN connection** → if you connect WireGuard yourself (outside this app), it won't interfere — it only auto-disconnects tunnels it connected
- **Force Disconnect** → clears the "owned by this app" flag so it won't immediately reconnect

Connection status is verified by checking the `WireGuardTunnel$<name>` Windows service state directly (no shell commands spawned).

## Settings

Settings are stored in `%AppData%\WG-Autoconnect\settings.json`:

```json
{
  "WireGuardConfigPath": "C:\\path\\to\\your\\tunnel.conf",
  "WireGuardExePath": "C:\\Program Files\\WireGuard\\wireguard.exe",
  "MonitoredApps": ["outlook.exe", "slack.exe"],
  "PollIntervalMs": 5000,
  "GracePeriodSeconds": 10,
  "DisconnectOnExit": true
}
```

You can edit this file directly — changes are picked up automatically via a file watcher.

## Uninstalling

From the tray menu, click **Uninstall**, or run from a terminal:

```bash
WG-Autoconnect.exe --uninstall
```

This removes the Task Scheduler startup entry and deletes settings/logs from `%AppData%\WG-Autoconnect`. You'll be asked if you also want to delete the exe. Your WireGuard installation and `.conf` files are never touched.

## Building from Source

```bash
# Clone
git clone https://github.com/Artixskillz/WG-Autoconnect.git
cd WG-Autoconnect/app

# Build
dotnet build

# Publish single-file exe
dotnet publish -c Release

# Output: app/bin/Release/net8.0-windows/win-x64/publish/WG-Autoconnect.exe
```

**Requirements:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## License

MIT License — see [LICENSE](LICENSE) for details.

namespace WgAutoconnect;

public sealed class AppContext : ApplicationContext
{
    private AppSettings _settings = null!;
    private readonly VpnService _vpn = null!;

    // Tray
    private readonly NotifyIcon        _trayIcon = null!;
    private readonly ContextMenuStrip  _menu = null!;
    private readonly ToolStripMenuItem _statusItem = null!;
    private readonly ToolStripMenuItem _pauseItem = null!;
    private readonly ToolStripMenuItem _startupItem = null!;
    private Icon? _currentIcon;

    // Timers — WinForms timers fire on the UI thread, no cross-thread issues.
    private readonly System.Windows.Forms.Timer _pollTimer = null!;
    private readonly System.Windows.Forms.Timer _graceTimer = null!;
    private readonly System.Windows.Forms.Timer _reloadDebounce = null!;

    // Config file watcher
    private FileSystemWatcher? _fileWatcher;
    private readonly SynchronizationContext _syncContext = null!;

    // State
    private bool _isPaused;
    private bool _isTransitioning;
    private bool _isScriptConnected;
    private bool _disconnectPending;

    // Notification cooldown
    private string _lastBalloonMessage = "";
    private DateTime _lastBalloonTime = DateTime.MinValue;
    private static readonly TimeSpan BalloonCooldown = TimeSpan.FromSeconds(30);

    public AppContext()
    {
        _syncContext = SynchronizationContext.Current!;
        _settings   = SettingsService.Load();

        bool isFirstRun = SettingsService.Validate(_settings).Count > 0;
        if (isFirstRun)
        {
            using var form = new SetupForm(_settings);
            if (form.ShowDialog() != DialogResult.OK)
            {
                Application.Exit();
                return;
            }
            _settings = SettingsService.Load();
        }

        _vpn = new VpnService(_settings);

        // Build tray UI
        _currentIcon = IconRenderer.Create(TrayState.Disconnected);
        _menu        = new ContextMenuStrip();

        var header = new ToolStripMenuItem("WG-Autoconnect") { Enabled = false };
        _statusItem  = new ToolStripMenuItem("Checking...") { Enabled = false };
        _pauseItem   = new ToolStripMenuItem("Pause Monitoring", null, OnPause);
        _startupItem = new ToolStripMenuItem(
            StartupService.IsRegistered() ? "Disable Run at Startup" : "Run at Windows Startup",
            null, OnToggleStartup);

        _menu.Items.Add(header);
        _menu.Items.Add(_statusItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_pauseItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Force Connect",    null, OnForceConnect));
        _menu.Items.Add(new ToolStripMenuItem("Force Disconnect", null, OnForceDisconnect));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_startupItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Settings", null, (_, _) => OpenSettings()));
        _menu.Items.Add(new ToolStripMenuItem("View Log",  null, OnViewLog));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        _trayIcon = new NotifyIcon
        {
            Icon             = _currentIcon,
            Text             = "WG-Autoconnect",
            ContextMenuStrip = _menu,
            Visible          = true,
        };
        _trayIcon.DoubleClick += (_, _) => OpenSettings();

        // Timers
        _pollTimer  = new System.Windows.Forms.Timer { Interval = _settings.PollIntervalMs };
        _graceTimer = new System.Windows.Forms.Timer { Interval = Math.Max(1, _settings.GracePeriodSeconds * 1000) };
        _pollTimer.Tick  += (_, _) => CheckAndToggle();
        _graceTimer.Tick += OnGraceExpired;

        // Debounce timer for config file watcher reloads
        _reloadDebounce = new System.Windows.Forms.Timer { Interval = 500 };
        _reloadDebounce.Tick += (_, _) => { _reloadDebounce.Stop(); ReloadSettings(); };

        // Watch settings.json for external changes
        StartFileWatcher();

        Logger.Info($"Started | Tunnel: {_settings.TunnelName} | Watching: {string.Join(", ", _settings.MonitoredApps)}");
        CheckAndToggle();
        _pollTimer.Start();

        // First-run: offer to register startup task
        if (isFirstRun && !StartupService.IsRegistered())
        {
            var result = MessageBox.Show(
                "Would you like WG-Autoconnect to start automatically with Windows?\n\n" +
                "It will run elevated via Task Scheduler (no UAC prompt on login).",
                "WG-Autoconnect", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes && StartupService.Register())
            {
                _startupItem.Text = "Disable Run at Startup";
                Logger.Info("Added to Windows startup (first-run prompt).");
            }
        }

        // Check for updates (non-blocking)
        _ = UpdateChecker.CheckForUpdateAsync((tag, url) =>
            _syncContext.Post(_ =>
            {
                Logger.Info($"Update available: {tag}");
                ShowBalloon($"Update {tag} available! Right-click tray → check releases.", ToolTipIcon.Info);
            }, null));
    }

    // -------------------------------------------------------------------------
    // Core polling logic
    // -------------------------------------------------------------------------

    private async void CheckAndToggle()
    {
        if (_isPaused || _isTransitioning) return;

        try
        {
            var runningApps = GetRunningApps();
            bool appsRunning = runningApps.Count > 0;
            bool vpnUp       = _vpn.IsConnected();
            UpdateStatus(vpnUp, runningApps);

            if (appsRunning)
            {
                if (_disconnectPending)
                {
                    _graceTimer.Stop();
                    _disconnectPending = false;
                    Logger.Info("Grace-period disconnect cancelled \u2014 app came back.");
                }
                if (!vpnUp)
                    await DoConnect();
                else
                    _isScriptConnected = true;
            }
            else
            {
                if (vpnUp && !_disconnectPending && _isScriptConnected)
                {
                    _disconnectPending   = true;
                    _graceTimer.Interval = Math.Max(1, _settings.GracePeriodSeconds * 1000);
                    _graceTimer.Start();
                    Logger.Info($"Apps closed. Disconnecting in {_settings.GracePeriodSeconds}s (grace period).");
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"CheckAndToggle failed: {ex.Message}");
        }
    }

    private async void OnGraceExpired(object? sender, EventArgs e)
    {
        _graceTimer.Stop();
        _disconnectPending = false;
        try
        {
            if (GetRunningApps().Count == 0 && _vpn.IsConnected() && !_isTransitioning)
                await DoDisconnect();
        }
        catch (Exception ex)
        {
            Logger.Error($"Grace period disconnect failed: {ex.Message}");
        }
    }

    private async Task DoConnect()
    {
        _isTransitioning   = true;
        _isScriptConnected = true;
        UpdateStatus(null);
        ShowBalloon($"Connecting to {_settings.TunnelName}...");
        Logger.Info($"Connecting | Tunnel: {_settings.TunnelName}");

        await _vpn.ConnectAsync();
        bool ok = await _vpn.WaitForConnected();

        if (!ok)
        {
            Logger.Info("Connect not confirmed, retrying...");
            await _vpn.ConnectAsync();
            ok = await _vpn.WaitForConnected();
        }

        _isTransitioning = false;
        if (ok)
        {
            Logger.Info($"Connection verified | Tunnel: {_settings.TunnelName}");
            UpdateStatus(true);
            ShowBalloon($"Connected to {_settings.TunnelName}.");
        }
        else
        {
            Logger.Error("VPN failed to connect after retry.");
            _isScriptConnected = false;
            UpdateStatus(false);
            ShowBalloon($"Failed to connect to {_settings.TunnelName}.", ToolTipIcon.Error);
        }
    }

    private async Task DoDisconnect()
    {
        _isTransitioning = true;
        UpdateStatus(null);
        ShowBalloon($"Disconnecting from {_settings.TunnelName}...");
        Logger.Info($"Disconnecting | Tunnel: {_settings.TunnelName}");

        await _vpn.DisconnectAsync();
        bool ok = await _vpn.WaitForDisconnected();

        if (!ok)
        {
            Logger.Info("Disconnect not confirmed, retrying...");
            await _vpn.DisconnectAsync();
            ok = await _vpn.WaitForDisconnected();
        }

        _isTransitioning   = false;
        _isScriptConnected = false;
        UpdateStatus(_vpn.IsConnected());

        if (ok)
        {
            Logger.Info($"Disconnect verified | Tunnel: {_settings.TunnelName}");
            ShowBalloon($"Disconnected from {_settings.TunnelName}.");
        }
        else
        {
            Logger.Error("VPN failed to disconnect after retry.");
            ShowBalloon($"Failed to disconnect from {_settings.TunnelName}.", ToolTipIcon.Error);
        }
    }

    private List<string> GetRunningApps() =>
        _settings.MonitoredApps
            .Where(app => System.Diagnostics.Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(app)).Length > 0)
            .ToList();

    // -------------------------------------------------------------------------
    // Config file watcher
    // -------------------------------------------------------------------------

    private void StartFileWatcher()
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDir);
            _fileWatcher = new FileSystemWatcher(SettingsService.DataDir, "settings.json")
            {
                NotifyFilter        = NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
            };
            _fileWatcher.Changed += (_, _) =>
                _syncContext.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not start config file watcher: {ex.Message}");
        }
    }

    private void ReloadSettings()
    {
        var newSettings = SettingsService.Load();
        if (SettingsService.Validate(newSettings).Count > 0) return;
        _settings = newSettings;
        _vpn.UpdateSettings(newSettings);
        _pollTimer.Interval  = _settings.PollIntervalMs;
        _graceTimer.Interval = Math.Max(1, _settings.GracePeriodSeconds * 1000);
        Logger.Info("Settings reloaded (external change detected).");
    }

    // -------------------------------------------------------------------------
    // Tray menu handlers
    // -------------------------------------------------------------------------

    private void OnPause(object? sender, EventArgs e)
    {
        _isPaused = !_isPaused;
        if (_isPaused)
        {
            if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
            _pauseItem.Text = "Resume Monitoring";
            Logger.Info("Monitoring paused by user.");
        }
        else
        {
            _pauseItem.Text = "Pause Monitoring";
            Logger.Info("Monitoring resumed by user.");
            CheckAndToggle();
        }
        UpdateStatus(_vpn.IsConnected());
    }

    private async void OnForceConnect(object? sender, EventArgs e)
    {
        try
        {
            if (_isTransitioning) return;
            if (_vpn.IsConnected()) { ShowBalloon("VPN is already connected."); return; }
            if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
            Logger.Info("Force-connect by user.");
            await DoConnect();
        }
        catch (Exception ex) { Logger.Error($"Force connect error: {ex.Message}"); }
    }

    private async void OnForceDisconnect(object? sender, EventArgs e)
    {
        try
        {
            if (_isTransitioning) return;
            if (!_vpn.IsConnected()) { ShowBalloon("VPN is already disconnected."); return; }
            if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
            _isScriptConnected = false;
            Logger.Info("Force-disconnect by user.");
            await DoDisconnect();
        }
        catch (Exception ex) { Logger.Error($"Force disconnect error: {ex.Message}"); }
    }

    private void OnToggleStartup(object? sender, EventArgs e)
    {
        bool registered = StartupService.IsRegistered();
        if (registered)
        {
            if (StartupService.Unregister())
            {
                _startupItem.Text = "Run at Windows Startup";
                ShowBalloon("Removed from Windows startup.");
                Logger.Info("Removed from Windows startup.");
            }
            else ShowBalloon("Failed to remove startup task.", ToolTipIcon.Error);
        }
        else
        {
            if (StartupService.Register())
            {
                _startupItem.Text = "Disable Run at Startup";
                ShowBalloon("Added to Windows startup (elevated, no UAC prompt).");
                Logger.Info("Added to Windows startup.");
            }
            else ShowBalloon("Failed to register startup task.", ToolTipIcon.Error);
        }
    }

    private void OpenSettings()
    {
        using var form = new SetupForm(_settings, _vpn);
        if (form.ShowDialog() != DialogResult.OK) return;

        _settings = SettingsService.Load();
        _vpn.UpdateSettings(_settings);
        _pollTimer.Interval  = _settings.PollIntervalMs;
        _graceTimer.Interval = Math.Max(1, _settings.GracePeriodSeconds * 1000);
        Logger.Info("Settings updated by user.");
    }

    private void OnViewLog(object? sender, EventArgs e)
    {
        if (File.Exists(Logger.LogPath))
            System.Diagnostics.Process.Start("notepad.exe", Logger.LogPath);
        else
            ShowBalloon("No log file yet.");
    }

    private void OnExit(object? sender, EventArgs e)
    {
        _pollTimer.Stop();
        _graceTimer.Stop();

        if (_settings.DisconnectOnExit && _isScriptConnected && _vpn.IsConnected())
        {
            Logger.Info("Disconnecting VPN on exit...");
            _vpn.DisconnectSync();
        }

        Logger.Info("Exiting.");
        _trayIcon.Visible = false;
        Application.Exit();
    }

    // -------------------------------------------------------------------------
    // Icon and status helpers
    // -------------------------------------------------------------------------

    private void UpdateStatus(bool? vpnUp, List<string>? runningApps = null)
    {
        var state =
            _isTransitioning  ? TrayState.Transitioning :
            _isPaused         ? TrayState.Paused        :
            vpnUp == true     ? TrayState.Connected     :
                                TrayState.Disconnected;

        var vpnText = vpnUp switch { true => "Connected", null => "Transitioning...", _ => "Disconnected" };
        var monText = _isPaused ? "Paused" : "Active";
        var label   = $"Monitoring: {monText}  |  VPN: {vpnText}";

        _statusItem.Text = label;

        // Build tooltip — show which monitored apps are running
        var tooltip = $"WG-Autoconnect\n{label}";
        if (runningApps?.Count > 0)
        {
            var names = string.Join(", ", runningApps.Select(Path.GetFileNameWithoutExtension));
            tooltip += $"\nRunning: {names}";
        }
        _trayIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        var newIcon = IconRenderer.Create(state);
        _trayIcon.Icon = newIcon;
        _currentIcon?.Dispose();
        _currentIcon = newIcon;
    }

    private void ShowBalloon(string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        // Suppress duplicate notifications within cooldown window
        if (message == _lastBalloonMessage && DateTime.UtcNow - _lastBalloonTime < BalloonCooldown)
            return;

        _lastBalloonMessage = message;
        _lastBalloonTime    = DateTime.UtcNow;

        _trayIcon.BalloonTipTitle = "WG-Autoconnect";
        _trayIcon.BalloonTipText  = message;
        _trayIcon.BalloonTipIcon  = icon;
        _trayIcon.ShowBalloonTip(3000);
    }

    // -------------------------------------------------------------------------
    // Disposal
    // -------------------------------------------------------------------------

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _fileWatcher?.Dispose();
            _reloadDebounce?.Dispose();
            _pollTimer?.Dispose();
            _graceTimer?.Dispose();
            _currentIcon?.Dispose();
            _trayIcon?.Dispose();
            _menu?.Dispose();
        }
        base.Dispose(disposing);
    }
}

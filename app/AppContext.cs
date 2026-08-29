namespace WgAutoconnect;

public sealed class AppContext : ApplicationContext
{
    private const string ReleasesFallbackUrl = "https://github.com/Artixskillz/WG-Autoconnect/releases/latest";

    private AppSettings _settings;
    private readonly VpnService _vpn;

    // Tray
    private readonly NotifyIcon        _trayIcon;
    private readonly ContextMenuStrip  _menu;
    private readonly ToolStripMenuItem _statusItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _startupItem;

    // Timers — WinForms timers fire on the UI thread, no cross-thread issues.
    private readonly System.Windows.Forms.Timer _pollTimer;
    private readonly System.Windows.Forms.Timer _graceTimer;
    private readonly System.Windows.Forms.Timer _reloadDebounce;

    // Config file watcher
    private FileSystemWatcher? _fileWatcher;
    private readonly SynchronizationContext _syncContext;

    // State
    private bool _isPaused;
    private bool _isTransitioning;
    private bool _isScriptConnected;   // true only when THIS app connected the VPN
    private bool _userOverride;        // true when user manually connected/disconnected outside this app
    private bool _forcedConnect;       // true after Force Connect with no apps running — blocks the grace disconnect
    private bool _disconnectPending;
    private bool _lastVpnState;
    private bool _settingsOpen;
    private bool _shuttingDown;
    private List<string> _lastRunningApps = [];
    private TrayState? _lastTrayState;
    private string? _updateUrl;          // release page (browser fallback)
    private string? _updateSetupUrl;     // direct installer download (in-app update)
    private string? _updateTag;
    private bool _updateInProgress;
    private bool _lastBalloonIsUpdate;
    private AppSettings? _pendingSettings;   // settings change that arrived mid-transition

    // Notification cooldown
    private string _lastBalloonMessage = "";
    private DateTime _lastBalloonTime = DateTime.MinValue;
    private static readonly TimeSpan BalloonCooldown = TimeSpan.FromSeconds(30);

    public AppContext(AppSettings settings, bool isFirstRun, EventWaitHandle showSettingsSignal)
    {
        // Program.Main installs a WindowsFormsSynchronizationContext before we
        // are constructed, so Current is guaranteed non-null here.
        _syncContext = SynchronizationContext.Current!;
        _settings    = settings;
        _vpn         = new VpnService(_settings);

        // Resume ownership of a tunnel this app connected in a previous session
        // (survives restarts, crashes, and installer upgrades via the marker file).
        bool vpnUpAtStart = _vpn.IsConnected();
        if (ConnectionMarker.Exists())
        {
            if (vpnUpAtStart)
            {
                _isScriptConnected = true;
                // A forced connection stays forced across restarts — otherwise a
                // resumed tunnel would be grace-disconnected seconds after launch.
                _forcedConnect = ConnectionMarker.IsForced();
                Logger.Info("Resuming management of tunnel connected in a previous session.");
            }
            else if (!_vpn.ServiceExists())
            {
                // Service genuinely absent — stale marker from a failed install.
                // (A service merely in START_PENDING at boot keeps the marker;
                // DetectExternalChanges re-adopts it once it reaches RUNNING.)
                ConnectionMarker.Clear();
            }
        }
        _lastVpnState = vpnUpAtStart;

        // Build tray UI
        _menu = new ContextMenuStrip();

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
        _menu.Items.Add(new ToolStripMenuItem("Open Data Folder", null, OnOpenDataFolder));
        _menu.Items.Add(new ToolStripMenuItem("Copy Diagnostics", null, OnCopyDiagnostics));
        _menu.Items.Add(new ToolStripMenuItem("Check for Updates", null, OnCheckForUpdates));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Uninstall", null, OnUninstall));
        _menu.Items.Add(new ToolStripMenuItem("Exit", null, OnExit));

        _trayIcon = new NotifyIcon
        {
            Icon             = IconRenderer.Get(TrayState.Disconnected),
            Text             = "WG-Autoconnect",
            ContextMenuStrip = _menu,
            Visible          = true,
        };
        _trayIcon.DoubleClick       += (_, _) => OpenSettings();
        _trayIcon.BalloonTipClicked += OnBalloonClicked;

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

        // A second launched instance signals this handle instead of showing a
        // dead-end message box — surface our Settings window when it fires.
        StartShowSettingsListener(showSettingsSignal);

        // Heal a startup task that points at a stale exe path (old portable
        // copy after switching to the installer, or a moved exe).
        StartupService.HealRegistration();

        // Toast notifications (update toast has an "Install now" button).
        // Purge toasts left by a previous session — they'd be dead buttons.
        Notifier.Initialize();
        Notifier.ClearHistory();
        Notifier.UpdateInstallRequested += () =>
            _syncContext.Post(_ => BeginUpdateInstall(), null);

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
        _ = UpdateChecker.CheckForUpdateAsync((tag, url, setupUrl) =>
            _syncContext.Post(_ => AnnounceUpdate(tag, url, setupUrl), null));
    }

    private void AnnounceUpdate(string tag, string url, string? setupUrl)
    {
        _updateTag      = tag;
        _updateUrl      = string.IsNullOrEmpty(url) ? ReleasesFallbackUrl : url;
        _updateSetupUrl = setupUrl;
        Logger.Info($"Update available: {tag}");
        if (!Notifier.ShowUpdateToast(tag))
            ShowBalloon($"Update {tag} available — click this notification to install.", ToolTipIcon.Info, isUpdate: true);
    }

    // -------------------------------------------------------------------------
    // Core polling logic
    // -------------------------------------------------------------------------

    private async void CheckAndToggle()
    {
        if (_shuttingDown || _isPaused || _isTransitioning) return;

        try
        {
            var runningApps = GetRunningApps();
            bool appsRunning = runningApps.Count > 0;
            bool vpnUp       = _vpn.IsConnected();
            UpdateStatus(vpnUp, runningApps);
            DetectExternalChanges(vpnUp);

            if (appsRunning)
            {
                if (_forcedConnect)
                {
                    // An app is running — normal automation resumes; downgrade the
                    // persisted marker so a restart doesn't revive the forced flag.
                    _forcedConnect = false;
                    if (_isScriptConnected) ConnectionMarker.Set(forced: false);
                }
                if (_disconnectPending)
                {
                    _graceTimer.Stop();
                    _disconnectPending = false;
                    Logger.Info("Grace-period disconnect cancelled — app came back.");
                }
                if (!vpnUp && !_userOverride)
                    await DoConnect();
            }
            else
            {
                // All monitored apps closed — clear the manual override so
                // automation resumes next time an app launches
                _userOverride = false;

                if (vpnUp && !_disconnectPending && _isScriptConnected && !_forcedConnect)
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

    /// <summary>
    /// Detects VPN state changes made outside this app (WireGuard GUI, wg CLI)
    /// and backs off instead of fighting the user. Our own transitions update
    /// _lastVpnState when they complete, so they never register as edges here.
    /// </summary>
    private void DetectExternalChanges(bool vpnUp)
    {
        if (vpnUp != _lastVpnState)
        {
            if (vpnUp && !_isScriptConnected)
            {
                if (ConnectionMarker.Exists())
                {
                    // A tunnel WE installed reached RUNNING after our confirmation
                    // window (slow driver init, boot race) — re-adopt it rather
                    // than misclassifying our own connect as a manual one.
                    _isScriptConnected = true;
                    _forcedConnect     = ConnectionMarker.IsForced();
                    Logger.Info("Tunnel installed by this app reached running state — resuming management.");
                }
                else
                {
                    // VPN came up but we didn't do it — user connected manually
                    _userOverride = true;
                    Logger.Info("Manual VPN connection detected — automation will not interfere.");
                }
            }
            else if (!vpnUp)
            {
                if (_isScriptConnected)
                {
                    // The tunnel we connected was torn down externally —
                    // respect the user's action instead of reconnecting.
                    _isScriptConnected = false;
                    ConnectionMarker.Clear();
                    Logger.Info("Script-connected tunnel was disconnected externally — automation will not interfere.");
                }
                else
                    Logger.Info("Manual VPN disconnection detected — automation will not interfere.");
                _userOverride = true;

                // Ownership is gone — a still-armed grace disconnect must not
                // fire against whatever the user connects next.
                if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
            }
        }
        _lastVpnState = vpnUp;
    }

    private async void OnGraceExpired(object? sender, EventArgs e)
    {
        _graceTimer.Stop();
        _disconnectPending = false;
        try
        {
            // Re-verify ownership: it may have been relinquished while the
            // timer was armed (manual disconnect, settings change).
            if (_isScriptConnected && GetRunningApps().Count == 0 && _vpn.IsConnected() && !_isTransitioning)
                await DoDisconnect();
        }
        catch (Exception ex)
        {
            Logger.Error($"Grace period disconnect failed: {ex.Message}");
        }
    }

    private async Task DoConnect()
    {
        // Capture up front: ApplyPendingSettings in the finally can swap
        // _settings, and the post-finally messaging must name THIS tunnel.
        var tunnel = _settings.TunnelName;

        _isTransitioning = true;
        UpdateStatus(null);
        ShowBalloon($"Connecting to {tunnel}...");
        Logger.Info($"Connecting | Tunnel: {tunnel}");

        // Claim ownership BEFORE issuing the install: once /installtunnelservice
        // is spawned the tunnel WILL exist, and an exit/uninstall racing this
        // transition must still know to tear it down. If the connect lands after
        // our confirmation window, DetectExternalChanges re-adopts via this marker.
        ConnectionMarker.Set(forced: _forcedConnect);

        bool ok = false;
        try
        {
            await _vpn.ConnectAsync();
            ok = await _vpn.WaitForConnected();
            // Never retry during shutdown: this continuation can resume inside
            // the uninstaller's modal pumps AFTER teardown already ran, and a
            // retry here would reinstall the tunnel post-uninstall.
            if (!ok && !_shuttingDown)
            {
                Logger.Info("Connect not confirmed, retrying...");
                await _vpn.ConnectAsync();
                ok = await _vpn.WaitForConnected();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Connect failed: {ex.Message}");
        }
        finally
        {
            // Always unwind, even on exceptions — a stuck _isTransitioning
            // would permanently disable all automation and both Force buttons.
            // Ownership follows the ACTUAL service state, not the confirmation
            // flag. The marker survives a not-yet-RUNNING service (re-adoption
            // handles slow starts), but a DEFINITIVELY failed install — no
            // service at all — must release it, or a later manual connection
            // of the same tunnel would be mis-adopted and grace-disconnected.
            _isTransitioning   = false;
            _lastVpnState      = _vpn.IsConnected();
            _isScriptConnected = _lastVpnState;
            if (!_lastVpnState && !_vpn.ServiceExists()) ConnectionMarker.Clear();
            UpdateStatus(_lastVpnState);
            ApplyPendingSettings();
        }

        if (ok)
        {
            Logger.Info($"Connection verified | Tunnel: {tunnel}");
            ShowBalloon($"Connected to {tunnel}.");
        }
        else if (_shuttingDown)
        {
            // Retry was deliberately skipped — don't log a phantom retry
            // failure or flash an error balloon during exit/uninstall.
            Logger.Info("Connect unconfirmed at shutdown — retry skipped; teardown will handle the tunnel.");
        }
        else
        {
            Logger.Error("VPN failed to connect after retry.");
            ShowBalloon($"Failed to connect to {tunnel}.", ToolTipIcon.Error);
        }
    }

    private async Task DoDisconnect()
    {
        // Capture up front — see DoConnect.
        var tunnel = _settings.TunnelName;

        _isTransitioning = true;
        UpdateStatus(null);
        ShowBalloon($"Disconnecting from {tunnel}...");
        Logger.Info($"Disconnecting | Tunnel: {tunnel}");

        bool ok = false;
        try
        {
            await _vpn.DisconnectAsync();
            ok = await _vpn.WaitForDisconnected();
            if (!ok && !_shuttingDown)   // see DoConnect — no retries during shutdown
            {
                Logger.Info("Disconnect not confirmed, retrying...");
                await _vpn.DisconnectAsync();
                ok = await _vpn.WaitForDisconnected();
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"Disconnect failed: {ex.Message}");
        }
        finally
        {
            // If the tunnel is STILL up (uninstall failed, service stuck),
            // keep ownership so later polls retry the disconnect and
            // exit/uninstall teardown still fire — but only if it was OURS
            // (marker): a failed Force Disconnect of a user-connected tunnel
            // must not adopt it. Only a confirmed teardown releases the marker.
            _isTransitioning   = false;
            _lastVpnState      = _vpn.IsConnected();
            _isScriptConnected = _lastVpnState && ConnectionMarker.Exists();
            if (!_lastVpnState) ConnectionMarker.Clear();
            UpdateStatus(_lastVpnState);
            ApplyPendingSettings();
        }

        if (ok)
        {
            Logger.Info($"Disconnect verified | Tunnel: {tunnel}");
            ShowBalloon($"Disconnected from {tunnel}.");
        }
        else if (_shuttingDown)
        {
            Logger.Info("Disconnect unconfirmed at shutdown — retry skipped; teardown will handle the tunnel.");
        }
        else
        {
            Logger.Error("VPN failed to disconnect after retry.");
            ShowBalloon($"Failed to disconnect from {tunnel}.", ToolTipIcon.Error);
        }
    }

    /// <summary>One process-table snapshot for all monitored apps; handles disposed.</summary>
    private List<string> GetRunningApps()
    {
        var monitored = new HashSet<string>(
            _settings.MonitoredApps.Select(a => Path.GetFileNameWithoutExtension(a)!),
            StringComparer.OrdinalIgnoreCase);

        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var procs = System.Diagnostics.Process.GetProcesses();
        try
        {
            foreach (var p in procs)
                if (monitored.Contains(p.ProcessName))
                    found.Add(p.ProcessName);
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }
        return [.. found];
    }

    // -------------------------------------------------------------------------
    // Config file watcher + settings application
    // -------------------------------------------------------------------------

    private void StartFileWatcher()
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDir);
            _fileWatcher = new FileSystemWatcher(SettingsService.DataDir, "settings.json")
            {
                // FileName + CreationTime + Size so rename-based saves
                // (VS Code and most editors) also trigger a reload.
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
                             | NotifyFilters.CreationTime | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            void QueueReload() =>
                _syncContext.Post(_ => { _reloadDebounce.Stop(); _reloadDebounce.Start(); }, null);
            _fileWatcher.Changed += (_, _) => QueueReload();
            _fileWatcher.Created += (_, _) => QueueReload();
            _fileWatcher.Renamed += (_, _) => QueueReload();
            _fileWatcher.Error   += (_, e) => Logger.Warn($"Settings watcher error: {e.GetException().Message}");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not start config file watcher: {ex.Message}");
        }
    }

    private void ReloadSettings()
    {
        var newSettings = SettingsService.Load();
        if (SettingsService.Validate(newSettings).Count > 0)
        {
            Logger.Warn("External settings change ignored — new settings failed validation.");
            return;
        }
        ApplySettings(newSettings);
        UpdateStatus(_vpn.IsConnected());
        Logger.Info("Settings reloaded (external change detected).");
    }

    /// <summary>Applies new settings, tearing down the old tunnel first if it changed while we own it.</summary>
    private void ApplySettings(AppSettings newSettings)
    {
        // Never swap settings under an in-flight transition: DoConnect would
        // resume against the NEW tunnel name, orphan the old tunnel, and
        // install both. The transition's finally applies the pending settings.
        if (_isTransitioning)
        {
            _pendingSettings = newSettings;
            Logger.Info("Settings change deferred until the current VPN transition completes.");
            return;
        }

        if (_isScriptConnected
            && !string.Equals(_settings.TunnelName, newSettings.TunnelName, StringComparison.OrdinalIgnoreCase)
            && _vpn.IsConnected())
        {
            // Without this, the old tunnel would be orphaned — never uninstalled —
            // and the next poll would install a second tunnel on top of it.
            Logger.Info($"Tunnel changed ({_settings.TunnelName} → {newSettings.TunnelName}) — disconnecting old tunnel.");
            _vpn.DisconnectSync();          // _vpn still holds the old settings
            _isScriptConnected = false;
            ConnectionMarker.Clear();
            _lastVpnState = false;
            if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
        }

        _settings = newSettings;
        _vpn.UpdateSettings(newSettings);
        _pollTimer.Interval  = _settings.PollIntervalMs;
        _graceTimer.Interval = Math.Max(1, _settings.GracePeriodSeconds * 1000);
    }

    /// <summary>Applies a settings change that arrived while a transition was in flight.</summary>
    private void ApplyPendingSettings()
    {
        // Never retarget _vpn/_settings once shutdown has begun: the exit and
        // uninstall teardown gates must keep checking the tunnel the in-flight
        // transition actually installed, not a renamed one. The pending change
        // is moot — it's already on disk and the next launch will load it.
        if (_shuttingDown) return;
        if (_pendingSettings == null) return;
        var pending = _pendingSettings;
        _pendingSettings = null;
        ApplySettings(pending);
        UpdateStatus(_vpn.IsConnected());
        Logger.Info("Deferred settings change applied.");
    }

    // -------------------------------------------------------------------------
    // Second-instance activation
    // -------------------------------------------------------------------------

    private void StartShowSettingsListener(EventWaitHandle signal)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    signal.WaitOne();
                    _syncContext.Post(_ => OpenSettings(), null);
                }
                catch (ObjectDisposedException) { return; }
                catch (InvalidOperationException) { return; }   // context's marshaling control destroyed during shutdown
            }
        })
        { IsBackground = true, Name = "ShowSettingsSignal" };
        thread.Start();
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
            if (_shuttingDown || _isTransitioning) return;
            if (_vpn.IsConnected()) { ShowBalloon("VPN is already connected."); return; }
            if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
            _userOverride  = false;
            _forcedConnect = true;   // survives "no apps running" polls — no grace disconnect
            Logger.Info("Force-connect by user.");
            await DoConnect();
        }
        catch (Exception ex) { Logger.Error($"Force connect error: {ex.Message}"); }
    }

    private async void OnForceDisconnect(object? sender, EventArgs e)
    {
        try
        {
            if (_shuttingDown || _isTransitioning) return;
            if (!_vpn.IsConnected()) { ShowBalloon("VPN is already disconnected."); return; }
            if (_disconnectPending) { _graceTimer.Stop(); _disconnectPending = false; }
            _userOverride  = false;
            _forcedConnect = false;
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
        if (_shuttingDown) return;   // no modal dialogs while exit/uninstall teardown pumps events
        if (_settingsOpen) return;   // second-instance signal or double-click while already open

        // Re-read the Windows light/dark preference — this long-lived tray app
        // may outlive several theme switches, and the palette is captured at
        // control construction time.
        Theme.Initialize();
        _settingsOpen = true;

        // Suppress file watcher while the form is open to avoid
        // double-reload / disposal conflicts when Save writes settings.json
        if (_fileWatcher != null) _fileWatcher.EnableRaisingEvents = false;
        _reloadDebounce.Stop();

        try
        {
            using var form = new SetupForm(_settings, _vpn);
            if (form.ShowDialog() == DialogResult.OK)
            {
                ApplySettings(SettingsService.Load());
                UpdateStatus(_vpn.IsConnected());
                Logger.Info($"Settings updated by user | Tunnel: {_settings.TunnelName} | Watching: {string.Join(", ", _settings.MonitoredApps)}");
            }
        }
        catch (Exception ex)
        {
            Logger.Error($"OpenSettings error: {ex}");
        }
        finally
        {
            _settingsOpen = false;
            if (_fileWatcher != null) _fileWatcher.EnableRaisingEvents = true;
        }
    }

    private async void OnCheckForUpdates(object? sender, EventArgs e)
    {
        ShowBalloon("Checking for updates...");
        await UpdateChecker.CheckForUpdateAsync((tag, url, setupUrl) =>
            _syncContext.Post(_ => AnnounceUpdate(tag, url, setupUrl), null),
            () => _syncContext.Post(_ =>
                ShowBalloon("You're running the latest version."), null));
    }

    private void OnBalloonClicked(object? sender, EventArgs e)
    {
        // Only act when the balloon being clicked is the update one —
        // clicking "Connected to X." must not surprise-start an install.
        if (!_lastBalloonIsUpdate) return;
        BeginUpdateInstall();
    }

    // -------------------------------------------------------------------------
    // In-app update
    // -------------------------------------------------------------------------

    private async void BeginUpdateInstall()
    {
        if (_updateInProgress || _shuttingDown) return;

        // No direct installer asset — browser fallback
        if (string.IsNullOrEmpty(_updateSetupUrl))
        {
            OpenReleasePage();
            return;
        }

        _updateInProgress = true;
        try
        {
            ShowBalloon($"Downloading update {_updateTag}...");
            Logger.Info($"Downloading update from {_updateSetupUrl}");
            var dest = Path.Combine(Path.GetTempPath(), "WG-Autoconnect-Setup.exe");
            bool ok = await UpdateService.DownloadInstallerAsync(_updateSetupUrl, dest);
            if (!ok)
            {
                ShowBalloon("Update download failed — opening the release page instead.", ToolTipIcon.Warning);
                OpenReleasePage();
                return;
            }
            ExitForUpdate(dest);
        }
        finally
        {
            _updateInProgress = false;
        }
    }

    /// <summary>
    /// Launches the downloaded installer and exits. Deliberately does NOT
    /// disconnect the VPN regardless of DisconnectOnExit — the ownership
    /// marker carries management through the upgrade without bouncing the
    /// tunnel, and the new version resumes seamlessly.
    /// </summary>
    private void ExitForUpdate(string installerPath)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _pollTimer.Stop();
        _graceTimer.Stop();
        _disconnectPending = false;   // timer stopped — a live poll must be able to re-arm it
        SettleTransition();
        Notifier.ClearHistory();

        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(installerPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            // Installer failed to launch — fully restore normal operation:
            // clear the shutdown gate, apply any settings change that was
            // deferred during the settle window (the "next launch will load
            // it" assumption no longer holds), and resume polling.
            Logger.Error($"Could not launch installer: {ex.Message}");
            _shuttingDown = false;
            ApplyPendingSettings();
            _pollTimer.Start();
            ShowBalloon("Could not launch the installer — opening the release page instead.", ToolTipIcon.Warning);
            OpenReleasePage();
            return;
        }

        Logger.Info("Exiting for in-app update; installer launched.");
        _trayIcon.Visible = false;
        Application.Exit();
    }

    private void OpenReleasePage()
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo(_updateUrl ?? ReleasesFallbackUrl)
                { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not open release page: {ex.Message}");
        }
    }

    private void OnViewLog(object? sender, EventArgs e)
    {
        if (File.Exists(Logger.LogPath))
            System.Diagnostics.Process.Start("notepad.exe", Logger.LogPath);
        else
            ShowBalloon("No log file yet.");
    }

    private void OnOpenDataFolder(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDir);
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{SettingsService.DataDir}\"")
                { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not open data folder: {ex.Message}");
        }
    }

    /// <summary>
    /// Lets an in-flight connect/disconnect finish before shutdown teardown
    /// reads ownership state. The transition's continuations are posted to
    /// this thread's message loop, so we must pump while waiting.
    /// </summary>
    private void SettleTransition()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (_isTransitioning && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(50);
        }
    }

    private void OnCopyDiagnostics(object? sender, EventArgs e)
    {
        try
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== WG-Autoconnect Diagnostics ===");
            sb.AppendLine($"Version:       v{version?.ToString(3) ?? "?"}");
            sb.AppendLine($"OS:            {Environment.OSVersion.VersionString} ({(Environment.Is64BitOperatingSystem ? "x64" : "x86")})");
            sb.AppendLine($"Exe:           {Environment.ProcessPath}");
            sb.AppendLine($"Dark theme:    {Theme.IsDark}");
            sb.AppendLine($"Tunnel:        {_settings.TunnelName}");
            sb.AppendLine($"Config path:   {_settings.WireGuardConfigPath}");
            sb.AppendLine($"WireGuard exe: {_settings.WireGuardExePath} (exists: {File.Exists(_settings.WireGuardExePath)})");
            sb.AppendLine($"Monitored:     {string.Join(", ", _settings.MonitoredApps)}");
            sb.AppendLine($"Poll/grace:    {_settings.PollIntervalMs}ms / {_settings.GracePeriodSeconds}s | DisconnectOnExit: {_settings.DisconnectOnExit}");
            sb.AppendLine($"Service:       exists={_vpn.ServiceExists()} running={_vpn.IsConnected()}");
            sb.AppendLine($"State:         paused={_isPaused} transitioning={_isTransitioning} scriptConnected={_isScriptConnected} override={_userOverride} forced={_forcedConnect} marker={ConnectionMarker.Exists()}");
            sb.AppendLine($"Startup task:  registered={StartupService.IsRegistered()} cmd={StartupService.GetRegisteredCommand() ?? "-"}");
            sb.AppendLine($"Running now:   {string.Join(", ", _lastRunningApps.DefaultIfEmpty("(none)"))}");
            sb.AppendLine();
            sb.AppendLine("=== Last 60 log lines ===");
            try
            {
                if (File.Exists(Logger.LogPath))
                    foreach (var line in File.ReadLines(Logger.LogPath).TakeLast(60))
                        sb.AppendLine(line);
            }
            catch { sb.AppendLine("(log unavailable)"); }

            Clipboard.SetText(sb.ToString());
            ShowBalloon("Diagnostics copied to clipboard — paste into a GitHub issue.");
        }
        catch (Exception ex)
        {
            Logger.Error($"Copy diagnostics failed: {ex.Message}");
            ShowBalloon("Could not copy diagnostics.", ToolTipIcon.Error);
        }
    }

    private void OnUninstall(object? sender, EventArgs e)
    {
        if (_shuttingDown) return;
        // Confirm BEFORE touching anything — cancelling must be a no-op.
        // Re-check the flag after the modal returns: its message pump can have
        // run a second Uninstall (or Exit) to completion while it was open.
        if (!Uninstaller.Confirm() || _shuttingDown) return;

        _shuttingDown = true;
        _pollTimer.Stop();
        _graceTimer.Stop();
        SettleTransition();
        _trayIcon.Visible = false;

        // Tear down with the LIVE session's settings — the on-disk settings
        // Execute would load can diverge (e.g. a rejected hand-edit). Gate on
        // ServiceExists, NOT IsConnected: a just-installed service still in
        // START_PENDING must be uninstalled too, or it becomes a permanent
        // unmanaged tunnel the moment it reaches RUNNING after we exit.
        // Only release the marker once the service is confirmed gone, so
        // Execute's disk-settings fallback still runs if this teardown fails.
        if (ConnectionMarker.Exists() && _vpn.ServiceExists())
            _vpn.DisconnectSync();
        // Keep the marker while a transition is provably still in flight — the
        // install child may not have created the service yet, and Execute's
        // 10s appearance-poll (which needs the marker) is what catches it.
        if (!_isTransitioning && !_vpn.ServiceExists())
            ConnectionMarker.Clear();

        // Execute handles any remaining teardown (via marker), removes the
        // startup task, and deletes app data. Pass the LIVE settings so the
        // fallback targets the tunnel this session actually manages even if
        // the on-disk settings have diverged (rejected edit, deferred rename).
        Uninstaller.Execute(interactive: true, _settings);

        // Final sweep: a transition that outlived the settle window could have
        // resumed during Execute's modal pumps — if the service reappeared,
        // remove it before exiting.
        if (_vpn.ServiceExists())
            _vpn.DisconnectSync();

        Application.Exit();
    }

    private void OnExit(object? sender, EventArgs e)
    {
        if (_shuttingDown) return;
        _shuttingDown = true;
        _pollTimer.Stop();
        _graceTimer.Stop();
        SettleTransition();
        Notifier.ClearHistory();   // a toast outliving the process is a dead button

        // Marker (not _isScriptConnected) is the durable ownership truth — it is
        // set BEFORE an install is issued. Gate on ServiceExists, not
        // IsConnected, so an install still in START_PENDING is torn down too.
        if (_settings.DisconnectOnExit && ConnectionMarker.Exists() && _vpn.ServiceExists())
        {
            Logger.Info("Disconnecting VPN on exit...");
            _vpn.DisconnectSync();
            if (!_vpn.ServiceExists()) ConnectionMarker.Clear();
        }
        // If DisconnectOnExit is off, the marker stays behind so the next
        // launch resumes management of the still-connected tunnel.

        Logger.Info("Exiting.");
        _trayIcon.Visible = false;
        Application.Exit();
    }

    // -------------------------------------------------------------------------
    // Icon and status helpers
    // -------------------------------------------------------------------------

    private void UpdateStatus(bool? vpnUp, List<string>? runningApps = null)
    {
        // Remember the last known running-apps list so the tooltip keeps its
        // "Running:" line when UpdateStatus is called without a fresh scan.
        if (runningApps != null) _lastRunningApps = runningApps;

        var state =
            _isTransitioning  ? TrayState.Transitioning :
            _isPaused         ? TrayState.Paused        :
            vpnUp == true     ? TrayState.Connected     :
                                TrayState.Disconnected;

        var vpnText = vpnUp switch { true => "Connected", null => "Transitioning...", _ => "Disconnected" };
        var monText = _isPaused ? "Paused" : "Active";
        var label   = $"Monitoring: {monText}  |  VPN: {vpnText}";

        _statusItem.Text = label;

        var tooltip = $"WG-Autoconnect\n{label}";
        if (_lastRunningApps.Count > 0)
            tooltip += $"\nRunning: {string.Join(", ", _lastRunningApps)}";
        _trayIcon.Text = tooltip.Length > 127 ? tooltip[..127] : tooltip;

        // Icons are cached per state — only touch the tray when the state changes.
        if (state != _lastTrayState)
        {
            _trayIcon.Icon = IconRenderer.Get(state);
            _lastTrayState = state;
        }
    }

    private void ShowBalloon(string message, ToolTipIcon icon = ToolTipIcon.Info, bool isUpdate = false)
    {
        // Suppress duplicate notifications within cooldown window
        if (message == _lastBalloonMessage && DateTime.UtcNow - _lastBalloonTime < BalloonCooldown)
            return;

        _lastBalloonMessage  = message;
        _lastBalloonTime     = DateTime.UtcNow;
        _lastBalloonIsUpdate = isUpdate;   // only the update balloon is click-to-download

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
            _trayIcon?.Dispose();
            _menu?.Dispose();
            // Cached tray icons (IconRenderer) intentionally live until process exit.
        }
        base.Dispose(disposing);
    }
}

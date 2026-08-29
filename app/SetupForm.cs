namespace WgAutoconnect;

/// <summary>
/// Settings window. Layout is entirely TableLayoutPanel/Dock/AutoSize-driven —
/// no absolute pixel positions — so it renders correctly at any display
/// scaling (100–200%) and rescales live when dragged between monitors.
/// </summary>
public class SetupForm : Form
{
    private readonly AppSettings _original;
    private readonly VpnService? _vpn;

    private ComboBox      _configCombo      = null!;
    private TextBox       _exePath          = null!;
    private ListBox       _appsList         = null!;
    private TextBox       _appEntry         = null!;
    private NumericUpDown _pollInterval     = null!;
    private NumericUpDown _gracePeriod      = null!;
    private CheckBox      _disconnectOnExit = null!;
    private StatusBanner? _statusBanner;
    private System.Windows.Forms.Timer? _statusTimer;

    public SetupForm(AppSettings settings, VpnService? vpn = null)
    {
        _original = settings;
        _vpn      = vpn;
        BuildUI();
        PopulateFields();

        if (_vpn != null)
        {
            UpdateLiveStatus();
            _statusTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _statusTimer.Tick += (_, _) => UpdateLiveStatus();
            _statusTimer.Start();
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statusTimer?.Stop();
        _statusTimer?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowTheme(this);
    }

    private void BuildUI()
    {
        // Layout MUST stay suspended until every control exists: assigning
        // AutoScaleDimensions with live layout consumes the one-shot DPI scale
        // on an empty form, and nothing added afterwards ever scales.
        SuspendLayout();

        Text            = "WG-Autoconnect";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = true;
        MinimizeBox     = true;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Theme.Background;
        Font            = Theme.Base;
        Icon            = IconRenderer.CreateFormIcon();
        MinimumSize     = new Size(500, 480);
        ClientSize      = new Size(560, _vpn != null ? 700 : 640);

        // ── Root: header / scrollable content / button bar ─────────
        var root = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 3,
            BackColor   = Theme.Background,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // header
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // content
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // buttons
        Controls.Add(root);

        var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
        var verText = version != null ? $"v{version.Major}.{version.Minor}.{version.Build}" : "";
        var header  = Theme.CreateHeader("WG-Autoconnect", $"Configure your WireGuard VPN automation  {verText}");
        header.Dock   = DockStyle.Fill;
        header.Margin = Padding.Empty;
        root.Controls.Add(header, 0, 0);

        // ── Scrollable stack of cards ──────────────────────────────
        var scroll = new Panel
        {
            Dock       = DockStyle.Fill,
            AutoScroll = true,
            Padding    = new Padding(16, 12, 16, 0),
            Margin     = Padding.Empty,
            BackColor  = Theme.Background,
        };
        root.Controls.Add(scroll, 0, 1);

        var stack = new TableLayoutPanel
        {
            Dock         = DockStyle.Top,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 1,
            BackColor    = Theme.Background,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scroll.Controls.Add(stack);

        // ── Live status banner (edit mode only) ────────────────────
        if (_vpn != null)
        {
            _statusBanner = new StatusBanner { Dock = DockStyle.Fill };
            stack.Controls.Add(_statusBanner);
        }

        stack.Controls.Add(BuildTunnelCard());
        stack.Controls.Add(BuildAppsCard());
        stack.Controls.Add(BuildOptionsCard());

        // ── Bottom buttons ─────────────────────────────────────────
        var buttons = new FlowLayoutPanel
        {
            Dock          = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize      = true,
            AutoSizeMode  = AutoSizeMode.GrowAndShrink,
            Padding       = new Padding(12, 6, 12, 10),
            BackColor     = Theme.Background,
            Margin        = Padding.Empty,
        };
        var btnSave   = Theme.PrimaryBtn("Save");
        var btnCancel = Theme.SecondaryBtn("Cancel");
        btnSave.Margin   = new Padding(6, 0, 0, 0);
        btnCancel.Margin = new Padding(6, 2, 0, 0);
        btnSave.Click   += OnSave;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(btnSave);     // rightmost
        buttons.Controls.Add(btnCancel);
        root.Controls.Add(buttons, 0, 2);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        // Designer pattern: scale dims set with the FULL tree present, then
        // resume — the deferred auto-scale pass now scales everything at once.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    // ── Card 1: tunnel + executable ──────────────────────────────

    private Card BuildTunnelCard()
    {
        var card = new Card("WireGuard Configuration") { Dock = DockStyle.Fill };

        card.Body.Controls.Add(SmallLabel("Tunnel  —  pick one imported in WireGuard, or browse to a .conf file"));

        var configRow = TwoColRow();
        _configCombo = new ComboBox
        {
            Dock          = DockStyle.Fill,
            DropDownStyle = ComboBoxStyle.DropDown,
            FlatStyle     = FlatStyle.Flat,
            BackColor     = Theme.InputBg,
            ForeColor     = Theme.TextPrimary,
            Margin        = new Padding(0, 2, 6, 2),
        };
        foreach (var choice in SettingsService.FindTunnelChoices(_original.WireGuardExePath))
            _configCombo.Items.Add(choice);
        var btnConf = Theme.SecondaryBtn("Browse…");
        btnConf.Click += (_, _) => BrowseConfig();
        configRow.Controls.Add(_configCombo, 0, 0);
        configRow.Controls.Add(btnConf, 1, 0);
        card.Body.Controls.Add(configRow);

        card.Body.Controls.Add(SmallLabel("WireGuard executable"));

        var exeRow = TwoColRow();
        _exePath = new TextBox
        {
            Dock        = DockStyle.Fill,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor   = Theme.InputBg,
            ForeColor   = Theme.TextPrimary,
            Margin      = new Padding(0, 2, 6, 2),
        };
        var btnExe = Theme.SecondaryBtn("Browse…");
        btnExe.Click += (_, _) => BrowseFor(_exePath, "Executables|wireguard.exe;*.exe|All Files|*.*");
        exeRow.Controls.Add(_exePath, 0, 0);
        exeRow.Controls.Add(btnExe, 1, 0);
        card.Body.Controls.Add(exeRow);

        return card;
    }

    // ── Card 2: monitored apps ───────────────────────────────────

    private Card BuildAppsCard()
    {
        var card = new Card("Monitored Applications") { Dock = DockStyle.Fill };

        card.Body.Controls.Add(SmallLabel("VPN connects when any of these processes are running:"));

        _appsList = new ListBox
        {
            Dock        = DockStyle.Fill,
            Height      = 104,
            BorderStyle = BorderStyle.FixedSingle,
            DrawMode    = DrawMode.OwnerDrawFixed,
            BackColor   = Theme.InputBg,
            ForeColor   = Theme.TextPrimary,
            Margin      = new Padding(0, 2, 0, 6),
        };
        void ScaleItems() => _appsList.ItemHeight = _appsList.LogicalToDeviceUnits(24);
        _appsList.HandleCreated         += (_, _) => ScaleItems();
        _appsList.DpiChangedAfterParent += (_, _) => ScaleItems();
        _appsList.DrawItem += DrawAppItem;
        card.Body.Controls.Add(_appsList);

        var entryRow = new TableLayoutPanel
        {
            Dock         = DockStyle.Fill,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 4,
            Margin       = Padding.Empty,
        };
        entryRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        entryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        entryRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        _appEntry = new TextBox
        {
            Dock            = DockStyle.Fill,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "e.g. slack.exe",
            BackColor       = Theme.InputBg,
            ForeColor       = Theme.TextPrimary,
            Margin          = new Padding(0, 4, 6, 2),
        };
        _appEntry.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddApp(); e.SuppressKeyPress = true; }
        };

        var btnAdd    = Theme.SecondaryBtn("Add");
        var btnPick   = Theme.SecondaryBtn("Pick…");
        var btnRemove = Theme.SecondaryBtn("Remove");
        btnPick.ForeColor = Theme.Primary;
        btnPick.Font      = Theme.BtnFont;
        foreach (var b in new[] { btnAdd, btnPick, btnRemove })
            b.Margin = new Padding(0, 2, 4, 0);

        btnAdd.Click    += (_, _) => AddApp();
        btnPick.Click   += (_, _) => PickFromRunning();
        btnRemove.Click += (_, _) =>
        {
            if (_appsList.SelectedIndex >= 0) _appsList.Items.RemoveAt(_appsList.SelectedIndex);
        };

        entryRow.Controls.Add(_appEntry, 0, 0);
        entryRow.Controls.Add(btnAdd, 1, 0);
        entryRow.Controls.Add(btnPick, 2, 0);
        entryRow.Controls.Add(btnRemove, 3, 0);
        card.Body.Controls.Add(entryRow);

        return card;
    }

    // ── Card 3: options ──────────────────────────────────────────

    private Card BuildOptionsCard()
    {
        var card = new Card("Options") { Dock = DockStyle.Fill };

        var grid = new TableLayoutPanel
        {
            Dock         = DockStyle.Fill,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 3,
            Margin       = Padding.Empty,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _pollInterval = new NumericUpDown
        {
            Width = 80, Minimum = 1000, Maximum = 60000, Increment = 1000,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.InputBg, ForeColor = Theme.TextPrimary,
            Margin = new Padding(8, 2, 0, 2),
        };
        _gracePeriod = new NumericUpDown
        {
            Width = 80, Minimum = 0, Maximum = 300, Increment = 5,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.InputBg, ForeColor = Theme.TextPrimary,
            Margin = new Padding(8, 2, 0, 2),
        };

        grid.Controls.Add(GridLabel("Poll interval"), 0, 0);
        grid.Controls.Add(_pollInterval, 1, 0);
        grid.Controls.Add(GridLabel("ms", secondary: true), 2, 0);

        grid.Controls.Add(GridLabel("Grace period"), 0, 1);
        grid.Controls.Add(_gracePeriod, 1, 1);
        grid.Controls.Add(GridLabel("seconds", secondary: true), 2, 1);

        _disconnectOnExit = new CheckBox
        {
            Text      = "Disconnect VPN when this app exits",
            AutoSize  = true,
            ForeColor = Theme.TextPrimary,
            FlatStyle = FlatStyle.Flat,
            Margin    = new Padding(0, 8, 0, 0),
        };
        grid.Controls.Add(_disconnectOnExit, 0, 2);
        grid.SetColumnSpan(_disconnectOnExit, 3);

        card.Body.Controls.Add(grid);
        return card;
    }

    // ── Live status ──────────────────────────────────────────────

    private void UpdateLiveStatus()
    {
        if (_vpn == null || _statusBanner == null) return;

        bool up = _vpn.IsConnected();

        // Reflect the app list as currently edited in the form (not the saved
        // settings), using a single process snapshot with disposed handles.
        var monitored = new HashSet<string>(
            _appsList.Items.Cast<string>().Select(a => Path.GetFileNameWithoutExtension(a)!),
            StringComparer.OrdinalIgnoreCase);

        var running = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var procs = System.Diagnostics.Process.GetProcesses();
        try
        {
            foreach (var p in procs)
                if (monitored.Contains(p.ProcessName))
                    running.Add(p.ProcessName);
        }
        finally
        {
            foreach (var p in procs) p.Dispose();
        }

        _statusBanner.SetState(up,
            up ? $"Connected to {_original.TunnelName}" : "Disconnected",
            running.Count > 0 ? $"Running: {string.Join(", ", running)}" : "No monitored apps running");
    }

    // ── Custom-drawn ListBox items (theme + DPI aware) ───────────

    private void DrawAppItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var text       = _appsList.Items[e.Index].ToString()!;
        var isSelected = (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(isSelected ? Theme.Primary : Theme.InputBg))
            e.Graphics.FillRectangle(bg, e.Bounds);

        using (var fg = new SolidBrush(isSelected ? Color.White : Theme.TextPrimary))
            e.Graphics.DrawString(text, e.Font ?? Theme.Base, fg,
                e.Bounds.Left + _appsList.LogicalToDeviceUnits(12),
                e.Bounds.Top + _appsList.LogicalToDeviceUnits(4));

        if ((e.State & DrawItemState.Focus) != 0 && !isSelected)
            ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds);
    }

    // ── Actions ──────────────────────────────────────────────────

    private void AddApp()
    {
        var app = _appEntry.Text.Trim();
        if (string.IsNullOrEmpty(app)) return;
        if (!app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app += ".exe";
        if (!_appsList.Items.Cast<string>().Any(
                a => a.Equals(app, StringComparison.OrdinalIgnoreCase)))
            _appsList.Items.Add(app);
        _appEntry.Clear();
    }

    private void PickFromRunning()
    {
        var existing = _appsList.Items.Cast<string>().ToList();
        using var picker = new ProcessPickerForm(existing);
        if (picker.ShowDialog(this) != DialogResult.OK) return;

        // Match on the extension-less stem, case-insensitively — stored entries
        // may be hand-edited ("slack", "Slack.EXE") while the picker normalizes
        // everything to "name.exe".
        static string Stem(string a) => Path.GetFileNameWithoutExtension(a);

        foreach (var app in picker.UncheckedApps)
        {
            var idx = _appsList.Items.Cast<string>().ToList()
                .FindIndex(a => Stem(a).Equals(Stem(app), StringComparison.OrdinalIgnoreCase));
            if (idx >= 0) _appsList.Items.RemoveAt(idx);
        }

        foreach (var app in picker.SelectedApps)
            if (!_appsList.Items.Cast<string>().Any(
                    a => Stem(a).Equals(Stem(app), StringComparison.OrdinalIgnoreCase)))
                _appsList.Items.Add(app);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var draft = new AppSettings
        {
            WireGuardConfigPath = SelectedConfigPath(),
            WireGuardExePath    = _exePath.Text.Trim(),
            MonitoredApps       = _appsList.Items.Cast<string>().ToList(),
        };

        var errors = SettingsService.Validate(draft);
        if (errors.Count > 0)
        {
            MessageBox.Show(string.Join("\n", errors), "Fix These Issues",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        draft.PollIntervalMs     = (int)_pollInterval.Value;
        draft.GracePeriodSeconds = (int)_gracePeriod.Value;
        draft.DisconnectOnExit   = _disconnectOnExit.Checked;
        SettingsService.Save(draft);
        DialogResult = DialogResult.OK;
    }

    /// <summary>The tunnel path to save: a picked TunnelChoice's path, or the raw typed text.</summary>
    private string SelectedConfigPath()
    {
        if (_configCombo.SelectedItem is TunnelChoice tc && _configCombo.Text == tc.Display)
            return tc.Path;
        return _configCombo.Text.Trim();
    }

    private void PopulateFields()
    {
        if (!string.IsNullOrEmpty(_original.WireGuardConfigPath))
        {
            var match = _configCombo.Items.Cast<TunnelChoice>()
                .FirstOrDefault(c => c.Path.Equals(_original.WireGuardConfigPath, StringComparison.OrdinalIgnoreCase));
            if (match != null) _configCombo.SelectedItem = match;
            else _configCombo.Text = _original.WireGuardConfigPath;
        }
        else if (_configCombo.Items.Count > 0)
            _configCombo.SelectedIndex = 0;

        _exePath.Text = File.Exists(_original.WireGuardExePath)
            ? _original.WireGuardExePath
            : SettingsService.FindWireGuardExe() ?? _original.WireGuardExePath;

        foreach (var app in _original.MonitoredApps) _appsList.Items.Add(app);

        _pollInterval.Value       = Math.Clamp(_original.PollIntervalMs,    1000, 60000);
        _gracePeriod.Value        = Math.Clamp(_original.GracePeriodSeconds,   0,   300);
        _disconnectOnExit.Checked = _original.DisconnectOnExit;
    }

    // ── Helpers ──────────────────────────────────────────────────

    private static Label SmallLabel(string text) => new()
    {
        Text = text, AutoSize = true, ForeColor = Theme.TextSecondary,
        Margin = new Padding(0, 6, 0, 2),
    };

    private static Label GridLabel(string text, bool secondary = false) => new()
    {
        Text = text, AutoSize = true,
        ForeColor = secondary ? Theme.TextSecondary : Theme.TextPrimary,
        Margin = new Padding(0, 6, 0, 0), Anchor = AnchorStyles.Left,
    };

    private static TableLayoutPanel TwoColRow()
    {
        var row = new TableLayoutPanel
        {
            Dock         = DockStyle.Fill,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 2,
            Margin       = Padding.Empty,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return row;
    }

    private void BrowseConfig()
    {
        using var dlg = new OpenFileDialog
        {
            Filter = "WireGuard Config|*.conf;*.conf.dpapi|All Files|*.*",
        };
        var current = SelectedConfigPath();
        if (!string.IsNullOrEmpty(current))
            try { dlg.InitialDirectory = Path.GetDirectoryName(current) ?? ""; } catch { }
        if (dlg.ShowDialog() != DialogResult.OK) return;
        _configCombo.Text = dlg.FileName;
    }

    private static void BrowseFor(TextBox target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        if (!string.IsNullOrEmpty(target.Text))
            try { dlg.InitialDirectory = Path.GetDirectoryName(target.Text) ?? ""; } catch { }
        if (dlg.ShowDialog() != DialogResult.OK) return;
        target.Text = dlg.FileName;
    }
}

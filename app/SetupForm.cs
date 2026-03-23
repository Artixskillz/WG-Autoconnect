using System.Drawing.Drawing2D;

namespace WgAutoconnect;

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

    // Live status
    private Panel? _statusBar;
    private System.Windows.Forms.Timer? _statusTimer;
    private bool   _liveVpnUp;
    private string _liveStatusText = "";
    private string _liveDetailText = "";

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

    private void BuildUI()
    {
        Text            = "WG-Autoconnect";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Theme.Background;
        Font            = Theme.Base;

        // ── Header ───────────────────────────────────────────────
        Controls.Add(Theme.CreateHeader("WG-Autoconnect", "Configure your WireGuard VPN automation"));

        const int cx  = 16;
        const int cw  = 498;
        const int ix  = 20;
        const int iw  = 462;
        const int ibw = 412;
        int y = 90;

        // ── Live status bar (only when editing, not first run) ───
        if (_vpn != null)
        {
            _statusBar = new Panel
            {
                Left = cx, Top = 82, Width = cw, Height = 52,
            };
            _statusBar.Paint += PaintStatusBar;
            Controls.Add(_statusBar);
            y = 144;
            ClientSize = new Size(530, 680);
        }
        else
        {
            ClientSize = new Size(530, 630);
        }

        // ══════════════════════════════════════════════════════════
        // Card 1 — WireGuard Configuration
        // ══════════════════════════════════════════════════════════
        var card1 = Theme.CreateCard(cx, y, cw, 136, "WireGuard Configuration");
        {
            int iy = 44;

            card1.Controls.Add(SmallLabel("Config file (.conf)", ix, iy));
            iy += 19;

            _configCombo = new ComboBox
            {
                Left = ix, Top = iy, Width = ibw,
                DropDownStyle = ComboBoxStyle.DropDown,
                FlatStyle     = FlatStyle.Flat,
                BackColor     = Color.White,
            };
            foreach (var f in SettingsService.FindConfFiles()) _configCombo.Items.Add(f);
            card1.Controls.Add(_configCombo);

            var btnConf = Theme.SecondaryBtn("\u2026", ix + ibw + 6, iy, 36, 24);
            btnConf.Click += (_, _) => BrowseFor(_configCombo, "WireGuard Config|*.conf|All Files|*.*");
            card1.Controls.Add(btnConf);
            iy += 30;

            card1.Controls.Add(SmallLabel("Executable", ix, iy));
            iy += 19;

            _exePath = new TextBox
            {
                Left = ix, Top = iy, Width = ibw,
                BorderStyle = BorderStyle.FixedSingle,
            };
            card1.Controls.Add(_exePath);

            var btnExe = Theme.SecondaryBtn("\u2026", ix + ibw + 6, iy, 36, 24);
            btnExe.Click += (_, _) => BrowseFor(_exePath, "Executables|wireguard.exe;*.exe|All Files|*.*");
            card1.Controls.Add(btnExe);
        }
        Controls.Add(card1);
        y += card1.Height + 10;

        // ══════════════════════════════════════════════════════════
        // Card 2 — Monitored Applications
        // ══════════════════════════════════════════════════════════
        var card2 = Theme.CreateCard(cx, y, cw, 194, "Monitored Applications");
        {
            int iy = 42;

            card2.Controls.Add(new Label
            {
                Text = "VPN connects when any of these processes are running:",
                Left = ix, Top = iy, Width = iw, Height = 18,
                ForeColor = Theme.TextSecondary,
            });
            iy += 22;

            _appsList = new ListBox
            {
                Left         = ix,
                Top          = iy,
                Width        = iw,
                Height       = 90,
                BorderStyle  = BorderStyle.FixedSingle,
                DrawMode     = DrawMode.OwnerDrawFixed,
                ItemHeight   = 24,
            };
            _appsList.DrawItem += DrawAppItem;
            card2.Controls.Add(_appsList);
            iy += 96;

            _appEntry = new TextBox
            {
                Left = ix, Top = iy, Width = 200,
                BorderStyle     = BorderStyle.FixedSingle,
                PlaceholderText = "e.g. slack.exe",
            };
            _appEntry.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter) { AddApp(); e.SuppressKeyPress = true; }
            };
            card2.Controls.Add(_appEntry);

            var btnAdd    = Theme.SecondaryBtn("Add",       ix + 206, iy - 1, 58, 26);
            var btnPick   = Theme.SecondaryBtn("Pick\u2026", ix + 268, iy - 1, 72, 26);
            var btnRemove = Theme.SecondaryBtn("Remove",    ix + 344, iy - 1, 72, 26);

            btnAdd.Click    += (_, _) => AddApp();
            btnPick.Click   += (_, _) => PickFromRunning();
            btnRemove.Click += (_, _) =>
            {
                if (_appsList.SelectedIndex >= 0) _appsList.Items.RemoveAt(_appsList.SelectedIndex);
            };

            btnPick.ForeColor = Theme.Primary;
            btnPick.Font = new Font("Segoe UI", 9f, FontStyle.Bold);

            card2.Controls.Add(btnAdd);
            card2.Controls.Add(btnPick);
            card2.Controls.Add(btnRemove);
        }
        Controls.Add(card2);
        y += card2.Height + 10;

        // ══════════════════════════════════════════════════════════
        // Card 3 — Options
        // ══════════════════════════════════════════════════════════
        var card3 = Theme.CreateCard(cx, y, cw, 130, "Options");
        {
            int iy = 46;

            card3.Controls.Add(new Label { Text = "Poll interval", Left = ix, Top = iy + 3, Width = 100, Height = 20, ForeColor = Theme.TextPrimary });
            _pollInterval = new NumericUpDown
            {
                Left = ix + 108, Top = iy, Width = 80,
                Minimum = 1000, Maximum = 60000, Increment = 1000,
                BorderStyle = BorderStyle.FixedSingle,
            };
            card3.Controls.Add(_pollInterval);
            card3.Controls.Add(new Label { Text = "ms", Left = ix + 194, Top = iy + 3, Width = 40, Height = 20, ForeColor = Theme.TextSecondary });
            iy += 30;

            card3.Controls.Add(new Label { Text = "Grace period", Left = ix, Top = iy + 3, Width = 100, Height = 20, ForeColor = Theme.TextPrimary });
            _gracePeriod = new NumericUpDown
            {
                Left = ix + 108, Top = iy, Width = 80,
                Minimum = 0, Maximum = 300, Increment = 5,
                BorderStyle = BorderStyle.FixedSingle,
            };
            card3.Controls.Add(_gracePeriod);
            card3.Controls.Add(new Label { Text = "seconds", Left = ix + 194, Top = iy + 3, Width = 60, Height = 20, ForeColor = Theme.TextSecondary });
            iy += 32;

            _disconnectOnExit = new CheckBox
            {
                Text = "Disconnect VPN when this app exits",
                Left = ix, Top = iy, Width = iw, Height = 22,
                ForeColor = Theme.TextPrimary,
                FlatStyle = FlatStyle.Flat,
            };
            card3.Controls.Add(_disconnectOnExit);
        }
        Controls.Add(card3);
        y += card3.Height + 16;

        // ── Bottom buttons ───────────────────────────────────────
        var btnCancel = Theme.SecondaryBtn("Cancel", cx + cw - 184, y, 86, 36);
        var btnSave   = Theme.PrimaryBtn("Save",     cx + cw - 92,  y, 92, 36);

        btnSave.Click   += OnSave;
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    // ── Live status bar painting ─────────────────────────────────

    private void PaintStatusBar(object? sender, PaintEventArgs e)
    {
        var g    = e.Graphics;
        var rect = _statusBar!.ClientRectangle;

        // Background tint based on VPN state
        var bgColor = _liveVpnUp
            ? Color.FromArgb(232, 245, 233)   // light green
            : Color.FromArgb(250, 250, 250);  // light gray
        using (var bg = new SolidBrush(bgColor))
            g.FillRectangle(bg, rect);

        // Bottom border
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, 0, rect.Height - 1, rect.Width, rect.Height - 1);

        // Status indicator dot
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var dotColor = _liveVpnUp
            ? Color.FromArgb(46, 125, 50)     // green
            : Color.FromArgb(158, 158, 158);  // gray
        using (var dotBrush = new SolidBrush(dotColor))
            g.FillEllipse(dotBrush, 20, 16, 16, 16);

        // Outer ring glow when connected
        if (_liveVpnUp)
        {
            using var glowPen = new Pen(Color.FromArgb(60, 46, 125, 50), 2);
            g.DrawEllipse(glowPen, 18, 14, 20, 20);
        }

        // Status text
        TextRenderer.DrawText(g, _liveStatusText, Theme.Section,
            new Point(44, 6), Theme.TextPrimary);

        TextRenderer.DrawText(g, _liveDetailText, Theme.Base,
            new Point(44, 28), Theme.TextSecondary);
    }

    private void UpdateLiveStatus()
    {
        if (_vpn == null || _statusBar == null) return;

        _liveVpnUp = _vpn.IsConnected();
        _liveStatusText = _liveVpnUp
            ? $"Connected to {_original.TunnelName}"
            : "Disconnected";

        var running = _original.MonitoredApps
            .Where(app => System.Diagnostics.Process.GetProcessesByName(
                Path.GetFileNameWithoutExtension(app)).Length > 0)
            .Select(a => Path.GetFileNameWithoutExtension(a))
            .ToList();

        _liveDetailText = running.Count > 0
            ? $"Running: {string.Join(", ", running)}"
            : "No monitored apps running";

        _statusBar.Invalidate();
    }

    // ── Custom-drawn ListBox items ───────────────────────────────

    private void DrawAppItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var text       = _appsList.Items[e.Index].ToString()!;
        var isSelected = (e.State & DrawItemState.Selected) != 0;

        using (var bg = new SolidBrush(isSelected ? Theme.Primary : Theme.Card))
            e.Graphics.FillRectangle(bg, e.Bounds);

        using (var fg = new SolidBrush(isSelected ? Color.White : Theme.TextPrimary))
            e.Graphics.DrawString(text, e.Font ?? Theme.Base, fg,
                e.Bounds.Left + 12, e.Bounds.Top + 4);

        if ((e.State & DrawItemState.Focus) != 0 && !isSelected)
            ControlPaint.DrawFocusRectangle(e.Graphics, e.Bounds);
    }

    // ── Actions ──────────────────────────────────────────────────

    private void AddApp()
    {
        var app = _appEntry.Text.Trim();
        if (string.IsNullOrEmpty(app)) return;
        if (!app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app += ".exe";
        if (!_appsList.Items.Contains(app)) _appsList.Items.Add(app);
        _appEntry.Clear();
    }

    private void PickFromRunning()
    {
        var existing = _appsList.Items.Cast<string>().ToList();
        using var picker = new ProcessPickerForm(existing);
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        foreach (var app in picker.SelectedApps)
            if (!_appsList.Items.Contains(app))
                _appsList.Items.Add(app);
    }

    private void OnSave(object? sender, EventArgs e)
    {
        var draft = new AppSettings
        {
            WireGuardConfigPath = _configCombo.Text.Trim(),
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

    private void PopulateFields()
    {
        if (!string.IsNullOrEmpty(_original.WireGuardConfigPath))
            _configCombo.Text = _original.WireGuardConfigPath;
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

    private static Label SmallLabel(string text, int left, int top) =>
        new() { Text = text, Left = left, Top = top, AutoSize = true, ForeColor = Theme.TextSecondary };

    private static void BrowseFor(Control target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter };
        var current = target is ComboBox cb ? cb.Text : ((TextBox)target).Text;
        if (!string.IsNullOrEmpty(current))
            try { dlg.InitialDirectory = Path.GetDirectoryName(current) ?? ""; } catch { }
        if (dlg.ShowDialog() != DialogResult.OK) return;
        if (target is ComboBox c) c.Text = dlg.FileName;
        else ((TextBox)target).Text = dlg.FileName;
    }
}

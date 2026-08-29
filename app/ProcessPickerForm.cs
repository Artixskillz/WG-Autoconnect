namespace WgAutoconnect;

/// <summary>
/// Running-process picker. Layout is container-driven (no absolute positions)
/// so it scales correctly at any DPI, and it follows the light/dark theme.
/// </summary>
public class ProcessPickerForm : Form
{
    private readonly CheckedListBox _processList;
    private readonly TextBox  _filter;
    private readonly CheckBox _showBackground;
    private readonly List<(string Name, bool HasWindow)> _allProcesses;
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _preChecked = new(StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "svchost", "csrss", "smss", "lsass", "services", "wininit", "winlogon",
        "dwm", "conhost", "System", "Registry", "Idle", "fontdrvhost",
        "sihost", "taskhostw", "RuntimeBroker", "SearchHost", "spoolsv",
        "ShellExperienceHost", "StartMenuExperienceHost", "TextInputHost",
        "SecurityHealthService", "SecurityHealthSystray", "dasHost",
        "WmiPrvSE", "dllhost", "backgroundTaskHost", "ctfmon", "wudfhost",
        "MsMpEng", "NisSrv", "SgrmBroker", "uhssvc", "SearchIndexer",
        "audiodg", "CompPkgSrv", "LsaIso", "MemCompression",
        "TrustedInstaller", "TabTip", "SearchProtocolHost",
        "SearchFilterHost", "WG-Autoconnect", "explorer",
        "SystemSettings", "WidgetService", "Widgets", "PhoneExperienceHost",
        "UserOOBEBroker", "GameBar", "GameBarFTServer",
    };

    /// <summary>Apps checked when the dialog was accepted.</summary>
    public List<string> SelectedApps { get; } = [];

    /// <summary>Already-monitored apps the user explicitly unchecked — remove these.</summary>
    public List<string> UncheckedApps { get; } = [];

    public ProcessPickerForm(IEnumerable<string> alreadyMonitored)
    {
        var already = new HashSet<string>(
            alreadyMonitored.Select(a => Path.GetFileNameWithoutExtension(a)),
            StringComparer.OrdinalIgnoreCase);

        // One snapshot; capture whether any instance of a name has a window so
        // the default view hides background noise. Dispose every handle.
        var snapshot = System.Diagnostics.Process.GetProcesses();
        var byName = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var p in snapshot)
            {
                string? name = null;
                bool hasWindow = false;
                try
                {
                    name = p.ProcessName;
                    hasWindow = p.MainWindowHandle != IntPtr.Zero;
                }
                catch { }
                if (name == null || SystemProcesses.Contains(name)) continue;
                byName[name] = byName.TryGetValue(name, out var had) ? had || hasWindow : hasWindow;
            }
        }
        finally
        {
            foreach (var p in snapshot) p.Dispose();
        }
        _allProcesses = byName
            .Select(kv => (kv.Key, kv.Value))
            .OrderBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var name in already)
        {
            _preChecked.Add(name + ".exe");
            _checked.Add(name + ".exe");
        }

        // ── Form ─────────────────────────────────────────────────
        // Suspend layout for the whole build — see SetupForm.BuildUI: the
        // one-shot DPI scale must not fire before the control tree exists.
        SuspendLayout();

        Text            = "WG-Autoconnect";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = true;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        BackColor       = Theme.Background;
        Font            = Theme.Base;
        ClientSize      = new Size(420, 560);
        MinimumSize     = new Size(360, 420);
        Icon            = IconRenderer.CreateFormIcon();

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

        var header = Theme.CreateHeader("Select Applications", "Choose running processes to monitor");
        header.Dock   = DockStyle.Fill;
        header.Margin = Padding.Empty;
        root.Controls.Add(header, 0, 0);

        // ── Content: filter row, background toggle, list ─────────
        var content = new TableLayoutPanel
        {
            Dock        = DockStyle.Fill,
            ColumnCount = 1,
            RowCount    = 3,
            Padding     = new Padding(16, 10, 16, 4),
            BackColor   = Theme.Background,
            Margin      = Padding.Empty,
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // filter
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // toggle
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // list
        root.Controls.Add(content, 0, 1);

        _filter = new TextBox
        {
            Dock            = DockStyle.Fill,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "Type to filter…",
            BackColor       = Theme.InputBg,
            ForeColor       = Theme.TextPrimary,
            Margin          = new Padding(0, 0, 0, 4),
        };
        _filter.TextChanged += (_, _) => ApplyFilter();
        content.Controls.Add(_filter, 0, 0);

        _showBackground = new CheckBox
        {
            Text      = "Show background processes",
            AutoSize  = true,
            ForeColor = Theme.TextSecondary,
            FlatStyle = FlatStyle.Flat,
            Margin    = new Padding(0, 0, 0, 4),
        };
        _showBackground.CheckedChanged += (_, _) => ApplyFilter();
        content.Controls.Add(_showBackground, 0, 1);

        _processList = new CheckedListBox
        {
            Dock         = DockStyle.Fill,
            CheckOnClick = true,
            BorderStyle  = BorderStyle.FixedSingle,
            BackColor    = Theme.InputBg,
            ForeColor    = Theme.TextPrimary,
            Margin       = Padding.Empty,
        };
        content.Controls.Add(_processList, 0, 2);

        _processList.ItemCheck += OnItemCheck;
        ApplyFilter();

        // ── Buttons ──────────────────────────────────────────────
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
        var btnOk     = Theme.PrimaryBtn("Apply");
        var btnCancel = Theme.SecondaryBtn("Cancel");
        btnOk.Margin     = new Padding(6, 0, 0, 0);
        btnCancel.Margin = new Padding(6, 2, 0, 0);

        btnOk.Click += (_, _) =>
        {
            SelectedApps.AddRange(_checked);
            // Pre-checked (already monitored) apps the user unchecked get removed.
            UncheckedApps.AddRange(_preChecked.Where(a => !_checked.Contains(a)));
            DialogResult = DialogResult.OK;
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        buttons.Controls.Add(btnOk);
        buttons.Controls.Add(btnCancel);
        root.Controls.Add(buttons, 0, 2);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode       = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.ApplyWindowTheme(this);
    }

    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        var item = _processList.Items[e.Index].ToString()!;
        if (e.NewValue == CheckState.Checked) _checked.Add(item);
        else _checked.Remove(item);
    }

    private void ApplyFilter()
    {
        var query   = _filter.Text.Trim();
        bool showAll = _showBackground.Checked;

        _processList.ItemCheck -= OnItemCheck;
        _processList.BeginUpdate();
        _processList.Items.Clear();
        foreach (var (name, hasWindow) in _allProcesses)
        {
            var display = name + ".exe";
            // Always show checked/monitored items so they can be unchecked;
            // otherwise hide windowless background processes unless requested.
            bool visible = showAll || hasWindow || _checked.Contains(display);
            if (!visible) continue;
            if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            _processList.Items.Add(display, _checked.Contains(display));
        }
        _processList.EndUpdate();
        _processList.ItemCheck += OnItemCheck;
    }
}

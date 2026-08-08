namespace WgAutoconnect;

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

        // ── Form ─────────────────────────────────────────────────
        Text            = "WG-Autoconnect";
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox     = true;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        ClientSize      = new Size(400, 560);
        MinimumSize     = new Size(360, 420);
        BackColor       = Theme.Background;
        Font            = Theme.Base;
        AutoScaleMode   = AutoScaleMode.Dpi;
        DoubleBuffered  = true;
        Icon            = IconRenderer.CreateFormIcon();

        Controls.Add(Theme.CreateHeader("Select Applications", "Choose running processes to monitor"));

        // ── Content card ─────────────────────────────────────────
        var card = Theme.CreateCard(16, 90, ClientSize.Width - 32, ClientSize.Height - 90 - 54, "Running Processes");
        card.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;

        card.Controls.Add(new Label
        {
            Text = "Filter:", Left = 20, Top = 44, Width = 42, Height = 18,
            ForeColor = Theme.TextSecondary,
        });

        _filter = new TextBox
        {
            Left = 64, Top = 42, Width = card.Width - 64 - 20,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "Type to filter…",
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _filter.TextChanged += (_, _) => ApplyFilter();
        card.Controls.Add(_filter);

        _showBackground = new CheckBox
        {
            Text = "Show background processes",
            Left = 20, Top = 68, Width = card.Width - 40, Height = 20,
            ForeColor = Theme.TextSecondary,
            FlatStyle = FlatStyle.Flat,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };
        _showBackground.CheckedChanged += (_, _) => ApplyFilter();
        card.Controls.Add(_showBackground);

        _processList = new CheckedListBox
        {
            Left         = 20,
            Top          = 94,
            Width        = card.Width - 40,
            Height       = card.Height - 94 - 12,
            CheckOnClick = true,
            BorderStyle  = BorderStyle.FixedSingle,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
        };
        card.Controls.Add(_processList);

        foreach (var name in already)
        {
            _preChecked.Add(name + ".exe");
            _checked.Add(name + ".exe");
        }
        _processList.ItemCheck += OnItemCheck;
        ApplyFilter();

        Controls.Add(card);

        // ── Buttons ──────────────────────────────────────────────
        var btnCancel = Theme.SecondaryBtn("Cancel", 0, 0, 86, 34);
        var btnOk     = Theme.PrimaryBtn("Apply", 0, 0, 96, 34);
        btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
        btnOk.Anchor     = AnchorStyles.Bottom | AnchorStyles.Right;
        btnCancel.Left   = ClientSize.Width - 16 - 96 - 6 - 86;
        btnOk.Left       = ClientSize.Width - 16 - 96;
        btnCancel.Top    = ClientSize.Height - 44;
        btnOk.Top        = ClientSize.Height - 44;

        btnOk.Click += (_, _) =>
        {
            SelectedApps.AddRange(_checked);
            // Pre-checked (already monitored) apps the user unchecked get removed.
            UncheckedApps.AddRange(_preChecked.Where(a => !_checked.Contains(a)));
            DialogResult = DialogResult.OK;
        };
        btnCancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        Controls.Add(btnOk);
        Controls.Add(btnCancel);
        AcceptButton = btnOk;
        CancelButton = btnCancel;
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

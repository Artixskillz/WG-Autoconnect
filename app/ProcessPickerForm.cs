namespace WgAutoconnect;

public class ProcessPickerForm : Form
{
    private readonly CheckedListBox _processList;
    private readonly TextBox _filter;
    private readonly List<string> _allProcesses;
    private readonly HashSet<string> _checked = new(StringComparer.OrdinalIgnoreCase);

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

    public List<string> SelectedApps { get; } = [];

    public ProcessPickerForm(IEnumerable<string> alreadyMonitored)
    {
        var already = new HashSet<string>(
            alreadyMonitored.Select(a => Path.GetFileNameWithoutExtension(a)),
            StringComparer.OrdinalIgnoreCase);

        _allProcesses = System.Diagnostics.Process.GetProcesses()
            .Select(p => { try { return p.ProcessName; } catch { return null; } })
            .Where(n => n != null && !SystemProcesses.Contains(n!))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // ── Form ─────────────────────────────────────────────────
        Text            = "WG-Autoconnect";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox     = false;
        MinimizeBox     = false;
        StartPosition   = FormStartPosition.CenterParent;
        ClientSize      = new Size(400, 530);
        BackColor       = Theme.Background;
        Font            = Theme.Base;
        AutoScaleMode   = AutoScaleMode.Dpi;
        DoubleBuffered  = true;
        Icon            = IconRenderer.CreateFormIcon();

        Controls.Add(Theme.CreateHeader("Select Applications", "Choose running processes to monitor"));

        // ── Content card ─────────────────────────────────────────
        var card = Theme.CreateCard(16, 90, 368, 386, "Running Processes");

        card.Controls.Add(new Label
        {
            Text = "Filter:", Left = 20, Top = 44, Width = 42, Height = 18,
            ForeColor = Theme.TextSecondary,
        });

        _filter = new TextBox
        {
            Left = 64, Top = 42, Width = 284,
            BorderStyle     = BorderStyle.FixedSingle,
            PlaceholderText = "Type to filter\u2026",
        };
        _filter.TextChanged += (_, _) => ApplyFilter();
        card.Controls.Add(_filter);

        _processList = new CheckedListBox
        {
            Left         = 20,
            Top          = 72,
            Width        = 328,
            Height       = 302,
            CheckOnClick = true,
            BorderStyle  = BorderStyle.FixedSingle,
        };
        card.Controls.Add(_processList);

        foreach (var name in _allProcesses)
        {
            var display = name + ".exe";
            bool isAlready = already.Contains(name);
            if (isAlready) _checked.Add(display);
            _processList.Items.Add(display, isAlready);
        }
        _processList.ItemCheck += OnItemCheck;

        Controls.Add(card);

        // ── Buttons ──────────────────────────────────────────────
        var btnCancel = Theme.SecondaryBtn("Cancel",       368 + 16 - 186, 488, 86, 34);
        var btnOk     = Theme.PrimaryBtn("Add Selected",  368 + 16 - 96,  488, 96, 34);

        btnOk.Click     += (_, _) => { SelectedApps.AddRange(_checked); DialogResult = DialogResult.OK; };
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
        var query = _filter.Text.Trim();
        _processList.ItemCheck -= OnItemCheck;
        _processList.Items.Clear();
        foreach (var name in _allProcesses)
        {
            if (query.Length > 0 && !name.Contains(query, StringComparison.OrdinalIgnoreCase))
                continue;
            var display = name + ".exe";
            _processList.Items.Add(display, _checked.Contains(display));
        }
        _processList.ItemCheck += OnItemCheck;
    }
}

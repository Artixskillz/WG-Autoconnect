using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace WgAutoconnect;

/// <summary>
/// WireGuard-branded color scheme with light/dark palettes, fonts, and
/// DPI-safe control factories. All factories produce AutoSize/Dock-driven
/// controls — never absolutely positioned ones — so layouts survive
/// 125/150/200% display scaling and per-monitor DPI changes.
/// </summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    /// <summary>Reads the Windows app theme. Call once at startup (and before opening forms).</summary>
    public static void Initialize()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            IsDark = key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { IsDark = false; }
    }

    // ── Palette (theme-aware) ────────────────────────────────────
    public static Color Primary       => IsDark ? Color.FromArgb(224, 92, 97)   : Color.FromArgb(136, 23, 26);
    public static Color PrimaryLight  => IsDark ? Color.FromArgb(238, 120, 124) : Color.FromArgb(172, 38, 42);
    public static Color Background    => IsDark ? Color.FromArgb(30, 30, 35)    : Color.FromArgb(242, 243, 245);
    public static Color Card          => IsDark ? Color.FromArgb(42, 42, 49)    : Color.White;
    public static Color InputBg       => IsDark ? Color.FromArgb(31, 31, 38)    : Color.White;
    public static Color TextPrimary   => IsDark ? Color.FromArgb(232, 232, 236) : Color.FromArgb(26, 26, 46);
    public static Color TextSecondary => IsDark ? Color.FromArgb(156, 163, 175) : Color.FromArgb(108, 117, 125);
    public static Color Border        => IsDark ? Color.FromArgb(62, 62, 70)    : Color.FromArgb(222, 226, 230);
    public static Color BtnBg         => IsDark ? Color.FromArgb(52, 52, 60)    : Color.FromArgb(245, 245, 247);
    public static Color BtnText       => IsDark ? Color.FromArgb(209, 213, 219) : Color.FromArgb(55, 65, 81);
    public static Color BtnHover      => IsDark ? Color.FromArgb(62, 62, 72)    : Color.FromArgb(232, 233, 237);

    // Banner is brand-dark red in both themes
    private static readonly Color HeaderGradA = Color.FromArgb(136, 23, 26);
    private static readonly Color HeaderGradB = Color.FromArgb(96, 16, 18);

    // ── Fonts (points — scaled automatically per monitor DPI) ────
    public static readonly Font Base     = new("Segoe UI", 9.5f);
    public static readonly Font Section  = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font Header   = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font Subtitle = new("Segoe UI", 9.5f);
    public static readonly Font BtnFont  = new("Segoe UI", 9f, FontStyle.Bold);

    // ── Dark title bar ───────────────────────────────────────────

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Applies the dark title bar when the dark theme is active. Call after handle creation.</summary>
    public static void ApplyWindowTheme(Form form)
    {
        if (!IsDark) return;
        try
        {
            // DWMWA_USE_IMMERSIVE_DARK_MODE is 20 from Win10 20H1 (19041);
            // builds 17763–18363 used the undocumented value 19.
            int attr = Environment.OSVersion.Version.Build >= 19041 ? 20 : 19;
            int dark = 1;
            DwmSetWindowAttribute(form.Handle, attr, ref dark, sizeof(int));
        }
        catch { }
    }

    // ── Buttons (AutoSize — no fixed pixel geometry) ─────────────

    public static Button PrimaryBtn(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14, 7, 14, 7),
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary, ForeColor = Color.White,
            Font = BtnFont, Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = PrimaryLight;
        return btn;
    }

    public static Button SecondaryBtn(string text)
    {
        var btn = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 4, 10, 4),
            FlatStyle = FlatStyle.Flat,
            BackColor = BtnBg, ForeColor = BtnText,
            Font = Base, Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = Border;
        btn.FlatAppearance.MouseOverBackColor = BtnHover;
        return btn;
    }

    // ── Header (gradient banner; geometry in logical units) ──────

    public static Panel CreateHeader(string title, string subtitle)
    {
        var header = new Panel { Dock = DockStyle.Top };
        void SizeHeader() => header.Height = header.LogicalToDeviceUnits(76);
        header.HandleCreated          += (_, _) => SizeHeader();
        header.DpiChangedAfterParent  += (_, _) => { SizeHeader(); header.Invalidate(); };
        header.Resize                 += (_, _) => header.Invalidate();
        header.Paint += (_, e) =>
        {
            using var gradient = new LinearGradientBrush(
                header.ClientRectangle, HeaderGradA, HeaderGradB,
                LinearGradientMode.ForwardDiagonal);
            e.Graphics.FillRectangle(gradient, header.ClientRectangle);

            int x = header.LogicalToDeviceUnits(24);
            TextRenderer.DrawText(e.Graphics, title, Header,
                new Point(x, header.LogicalToDeviceUnits(14)), Color.White);
            TextRenderer.DrawText(e.Graphics, subtitle, Subtitle,
                new Point(x, header.LogicalToDeviceUnits(44)), Color.FromArgb(200, 255, 255, 255));
        };
        return header;
    }
}

/// <summary>
/// Rounded card with accent stripe and section title. Content goes in
/// <see cref="Body"/> (a single-column TableLayoutPanel). Fully AutoSize:
/// the card grows to fit its content at any DPI.
/// </summary>
public sealed class Card : Panel
{
    public TableLayoutPanel Body { get; }

    public Card(string title)
    {
        BackColor      = Theme.Card;
        AutoSize       = true;
        AutoSizeMode   = AutoSizeMode.GrowAndShrink;
        DoubleBuffered = true;
        Margin         = new Padding(0, 0, 0, 12);

        Body = new TableLayoutPanel
        {
            Dock         = DockStyle.Top,
            AutoSize     = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount  = 1,
            BackColor    = Color.Transparent,
            Padding      = new Padding(20, 8, 20, 14),
        };
        Body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        var separator = new Panel
        {
            Dock = DockStyle.Top, Height = 1,
            BackColor = Theme.Border, Margin = Padding.Empty,
        };

        var titleLabel = new Label
        {
            Text = title, Font = Theme.Section, ForeColor = Theme.Primary,
            AutoSize = true, Dock = DockStyle.Top,
            Padding = new Padding(20, 12, 0, 8), BackColor = Color.Transparent,
        };

        // Dock=Top lays out from the END of the control collection, so add
        // bottom-to-top: Body, separator, then title ends up on top.
        Controls.Add(Body);
        Controls.Add(separator);
        Controls.Add(titleLabel);

        Paint  += PaintCard;
        Resize += (_, _) => Invalidate();
    }

    private void PaintCard(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        int w = Width, h = Height;
        int r = LogicalToDeviceUnits(6);

        using var path = new GraphicsPath();
        path.AddArc(0, 0, r, r, 180, 90);
        path.AddArc(w - r - 1, 0, r, r, 270, 90);
        path.AddArc(w - r - 1, h - r - 1, r, r, 0, 90);
        path.AddArc(0, h - r - 1, r, r, 90, 90);
        path.CloseFigure();

        using (var borderPen = new Pen(Color.FromArgb(Theme.IsDark ? 70 : 30, 0, 0, 0)))
            g.DrawPath(borderPen, path);

        using var brush = new SolidBrush(Theme.Primary);
        g.FillRectangle(brush, 0, r / 2, LogicalToDeviceUnits(4), h - r);
    }
}

/// <summary>
/// Live VPN status banner for the settings form: colored dot, status line,
/// detail line. All geometry in logical units so it scales with DPI.
/// </summary>
public sealed class StatusBanner : Panel
{
    private bool   _up;
    private string _status = "";
    private string _detail = "";

    public StatusBanner()
    {
        DoubleBuffered = true;
        Margin = new Padding(0, 0, 0, 12);
        void SizeBanner() => Height = LogicalToDeviceUnits(52);
        HandleCreated         += (_, _) => SizeBanner();
        DpiChangedAfterParent += (_, _) => { SizeBanner(); Invalidate(); };
        Resize                += (_, _) => Invalidate();
        Paint += PaintBanner;
    }

    public void SetState(bool vpnUp, string status, string detail)
    {
        _up = vpnUp; _status = status; _detail = detail;
        Invalidate();
    }

    private void PaintBanner(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;

        var bg = _up
            ? (Theme.IsDark ? Color.FromArgb(28, 46, 32)    : Color.FromArgb(232, 245, 233))
            : (Theme.IsDark ? Color.FromArgb(40, 40, 46)    : Color.FromArgb(250, 250, 250));
        using (var b = new SolidBrush(bg)) g.FillRectangle(b, ClientRectangle);
        using (var pen = new Pen(Theme.Border))
            g.DrawLine(pen, 0, Height - 1, Width, Height - 1);

        g.SmoothingMode = SmoothingMode.AntiAlias;
        var dotColor = _up
            ? (Theme.IsDark ? Color.FromArgb(102, 187, 106) : Color.FromArgb(46, 125, 50))
            : Color.FromArgb(158, 158, 158);
        int dotX = LogicalToDeviceUnits(20), dotY = LogicalToDeviceUnits(16), dotD = LogicalToDeviceUnits(16);
        using (var dot = new SolidBrush(dotColor)) g.FillEllipse(dot, dotX, dotY, dotD, dotD);
        if (_up)
        {
            using var glow = new Pen(Color.FromArgb(60, dotColor), LogicalToDeviceUnits(2));
            int pad = LogicalToDeviceUnits(2);
            g.DrawEllipse(glow, dotX - pad, dotY - pad, dotD + 2 * pad, dotD + 2 * pad);
        }

        int textX = LogicalToDeviceUnits(44);
        TextRenderer.DrawText(g, _status, Theme.Section,
            new Point(textX, LogicalToDeviceUnits(6)), Theme.TextPrimary);
        TextRenderer.DrawText(g, _detail, Theme.Base,
            new Point(textX, LogicalToDeviceUnits(28)), Theme.TextSecondary);
    }
}

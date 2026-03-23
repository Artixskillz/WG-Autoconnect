using System.Drawing.Drawing2D;

namespace WgAutoconnect;

/// <summary>WireGuard-branded color scheme, fonts, and control factories.</summary>
public static class Theme
{
    // ── WireGuard-inspired palette ───────────────────────────────
    public static readonly Color Primary      = Color.FromArgb(136, 23, 26);   // WG red
    public static readonly Color PrimaryDark   = Color.FromArgb(96,  16, 18);
    public static readonly Color PrimaryLight  = Color.FromArgb(172, 38, 42);
    public static readonly Color Background    = Color.FromArgb(242, 243, 245);
    public static readonly Color Card          = Color.White;
    public static readonly Color TextPrimary   = Color.FromArgb(26,  26, 46);
    public static readonly Color TextSecondary = Color.FromArgb(108, 117, 125);
    public static readonly Color Border        = Color.FromArgb(222, 226, 230);
    public static readonly Color BtnBg         = Color.FromArgb(245, 245, 247);
    public static readonly Color BtnText       = Color.FromArgb(55,  65,  81);
    public static readonly Color BtnHover      = Color.FromArgb(232, 233, 237);

    // ── Fonts ────────────────────────────────────────────────────
    public static readonly Font Base     = new("Segoe UI", 9.5f);
    public static readonly Font Section  = new("Segoe UI", 10f, FontStyle.Bold);
    public static readonly Font Header   = new("Segoe UI", 16f, FontStyle.Bold);
    public static readonly Font Subtitle = new("Segoe UI", 9.5f);
    public static readonly Font BtnFont  = new("Segoe UI", 9f, FontStyle.Bold);

    // ── Buttons ──────────────────────────────────────────────────

    public static Button PrimaryBtn(string text, int left, int top, int width = 90, int height = 36)
    {
        var btn = new Button
        {
            Text = text, Left = left, Top = top, Width = width, Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = Primary, ForeColor = Color.White,
            Font = BtnFont, Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 0;
        btn.FlatAppearance.MouseOverBackColor = PrimaryLight;
        return btn;
    }

    public static Button SecondaryBtn(string text, int left, int top, int width = 80, int height = 28)
    {
        var btn = new Button
        {
            Text = text, Left = left, Top = top, Width = width, Height = height,
            FlatStyle = FlatStyle.Flat,
            BackColor = BtnBg, ForeColor = BtnText,
            Font = Base, Cursor = Cursors.Hand,
        };
        btn.FlatAppearance.BorderSize = 1;
        btn.FlatAppearance.BorderColor = Border;
        btn.FlatAppearance.MouseOverBackColor = BtnHover;
        return btn;
    }

    // ── Header (gradient banner, text painted directly) ──────────

    public static Panel CreateHeader(string title, string subtitle)
    {
        var header = new Panel { Dock = DockStyle.Top, Height = 76 };
        header.Paint += (_, e) =>
        {
            using var gradient = new LinearGradientBrush(
                header.ClientRectangle, Primary, PrimaryDark,
                LinearGradientMode.ForwardDiagonal);
            e.Graphics.FillRectangle(gradient, header.ClientRectangle);

            TextRenderer.DrawText(e.Graphics, title, Header,
                new Point(24, 14), Color.White);
            TextRenderer.DrawText(e.Graphics, subtitle, Subtitle,
                new Point(24, 44), Color.FromArgb(200, 255, 255, 255));
        };
        return header;
    }

    // ── Card (white panel, left accent stripe, subtle depth) ─────

    public static Panel CreateCard(int left, int top, int width, int height, string title)
    {
        var card = new Panel
        {
            Left = left, Top = top, Width = width, Height = height,
            BackColor = Card,
        };
        card.Paint += (_, e) =>
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // Rounded clip region
            const int r = 6;
            using var path = new GraphicsPath();
            path.AddArc(0, 0, r, r, 180, 90);
            path.AddArc(width - r - 1, 0, r, r, 270, 90);
            path.AddArc(width - r - 1, height - r - 1, r, r, 0, 90);
            path.AddArc(0, height - r - 1, r, r, 90, 90);
            path.CloseFigure();

            // Subtle shadow/border
            using (var borderPen = new Pen(Color.FromArgb(30, 0, 0, 0)))
                g.DrawPath(borderPen, path);

            // Left accent stripe (clipped to rounded corner)
            using (var brush = new SolidBrush(Primary))
                g.FillRectangle(brush, 0, r / 2, 4, card.Height - r);
        };

        // Section title
        card.Controls.Add(new Label
        {
            Text = title, Font = Section, ForeColor = Primary,
            Left = 20, Top = 12, AutoSize = true, BackColor = Card,
        });

        // Thin separator
        card.Controls.Add(new Panel
        {
            Left = 20, Top = 35, Width = width - 36, Height = 1,
            BackColor = Border,
        });

        return card;
    }
}

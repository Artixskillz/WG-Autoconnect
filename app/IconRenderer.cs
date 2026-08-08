using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WgAutoconnect;

public enum TrayState { Connected, Disconnected, Transitioning, Paused }

public static class IconRenderer
{
    private static readonly Color ColorConnected     = Color.FromArgb(136, 23,  26);   // WG red
    private static readonly Color ColorDisconnected  = Color.FromArgb(107, 114, 128);  // gray
    private static readonly Color ColorTransitioning = Color.FromArgb(249, 115, 22);   // orange
    private static readonly Color ColorPaused        = Color.FromArgb(234, 179, 8);    // yellow

    // The tray icon is re-applied on every status update; caching one icon per
    // state avoids creating + destroying a GDI HICON every poll for the life of
    // the process. Cached icons live until process exit — never Dispose() them.
    private static readonly Dictionary<TrayState, Icon> Cache = [];

    public static Icon Get(TrayState state)
    {
        if (!Cache.TryGetValue(state, out var icon))
        {
            icon = Create(state);
            Cache[state] = icon;
        }
        return icon;
    }

    private static Icon Create(TrayState state)
    {
        var fill = state switch
        {
            TrayState.Connected     => ColorConnected,
            TrayState.Transitioning => ColorTransitioning,
            TrayState.Paused        => ColorPaused,
            _                       => ColorDisconnected,
        };

        // Render at the system small-icon size so the icon is crisp at
        // 125%/150% DPI instead of a stretched 16x16 bitmap.
        int size = Math.Max(16, SystemInformation.SmallIconSize.Width);
        return Render(size, fill, 6.5f * size / 16f);
    }

    private static Icon? _formIcon;

    /// <summary>32x32 icon for Form windows. Cached for process lifetime — do NOT Dispose().</summary>
    public static Icon CreateFormIcon() => _formIcon ??= Render(32, ColorConnected, 13f);

    private static Icon Render(int size, Color fill, float fontSize)
    {
        using var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode     = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            float margin = size / 16f;
            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, margin, margin, size - 2 * margin, size - 2 * margin);

            using var font = new Font("Segoe UI", fontSize, FontStyle.Bold,
                size >= 32 ? GraphicsUnit.Pixel : GraphicsUnit.Point);
            using var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("W", font, Brushes.White, new RectangleF(0, margin / 2, size, size - margin / 2), sf);
        }

        // GetHicon() creates a GDI HICON. Icon.FromHandle() wraps it but does NOT take ownership.
        // Clone() produces a fully managed independent copy; we then destroy the raw handle.
        IntPtr hicon   = bmp.GetHicon();
        using var wrap = Icon.FromHandle(hicon);
        Icon owned     = (Icon)wrap.Clone();
        DestroyIcon(hicon);
        return owned;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

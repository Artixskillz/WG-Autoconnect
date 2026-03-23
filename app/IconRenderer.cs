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

    public static Icon Create(TrayState state)
    {
        var fill = state switch
        {
            TrayState.Connected     => ColorConnected,
            TrayState.Transitioning => ColorTransitioning,
            TrayState.Paused        => ColorPaused,
            _                       => ColorDisconnected,
        };

        using var bmp = new Bitmap(16, 16, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using var brush = new SolidBrush(fill);
            g.FillEllipse(brush, 1, 1, 14, 14);

            using var font = new Font("Segoe UI", 6.5f, FontStyle.Bold);
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("W", font, Brushes.White, new RectangleF(0, 1, 16, 15), sf);
        }

        // GetHicon() creates a GDI HICON. Icon.FromHandle() wraps it but does NOT take ownership.
        // Clone() produces a fully managed independent copy; we then destroy the raw handle.
        IntPtr hicon   = bmp.GetHicon();
        using var wrap = Icon.FromHandle(hicon);
        Icon owned     = (Icon)wrap.Clone();
        DestroyIcon(hicon);
        return owned;  // caller must Dispose() this
    }

    /// <summary>Creates a 32x32 icon for use as a Form window icon.</summary>
    public static Icon CreateFormIcon()
    {
        using var bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            using var brush = new SolidBrush(ColorConnected);
            g.FillEllipse(brush, 2, 2, 28, 28);

            using var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            var sf = new StringFormat
            {
                Alignment     = StringAlignment.Center,
                LineAlignment = StringAlignment.Center,
            };
            g.DrawString("W", font, Brushes.White, new RectangleF(0, 1, 32, 31), sf);
        }

        IntPtr hicon   = bmp.GetHicon();
        using var wrap = Icon.FromHandle(hicon);
        Icon owned     = (Icon)wrap.Clone();
        DestroyIcon(hicon);
        return owned;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

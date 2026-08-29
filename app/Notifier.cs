using Microsoft.Toolkit.Uwp.Notifications;

namespace WgAutoconnect;

/// <summary>
/// Modern toast notifications with graceful degradation. Routine status stays
/// on tray balloons (transient, no Action Center clutter); toasts are used
/// where their action buttons add value — the update notification. Any toast
/// failure permanently falls back to balloons for the session.
/// </summary>
public static class Notifier
{
    private static bool _available = true;

    /// <summary>Raised (on a background thread) when the user clicks "Install now" on the update toast.</summary>
    public static event Action? UpdateInstallRequested;

    public static void Initialize()
    {
        try
        {
            ToastNotificationManagerCompat.OnActivated += toastArgs =>
            {
                try
                {
                    var args = ToastArguments.Parse(toastArgs.Argument);
                    if (args.TryGetValue("action", out string action) && action == "install-update")
                        UpdateInstallRequested?.Invoke();
                }
                catch { }
            };
        }
        catch (Exception ex)
        {
            _available = false;
            Logger.Warn($"Toast notifications unavailable ({ex.Message}) — using tray balloons.");
        }
    }

    /// <summary>Update toast with Install/Later buttons. Returns false if toasts are unavailable.</summary>
    public static bool ShowUpdateToast(string tag)
    {
        if (!_available) return false;
        try
        {
            new ToastContentBuilder()
                .AddText("WG-Autoconnect update available")
                .AddText($"Version {tag} is ready — install it now?")
                .AddButton(new ToastButton()
                    .SetContent("Install now")
                    .AddArgument("action", "install-update"))
                .AddButton(new ToastButtonDismiss("Later"))
                .Show();
            return true;
        }
        catch (Exception ex)
        {
            _available = false;
            Logger.Warn($"Toast failed ({ex.Message}) — using tray balloons.");
            return false;
        }
    }

    /// <summary>
    /// Removes any of our toasts from the Action Center. Called at startup and
    /// on exit: a toast that outlives the process is a dead button — DCOM
    /// cannot cold-activate a requireAdministrator exe (no UAC prompt is ever
    /// shown), so a stale "Install now" would silently do nothing.
    /// </summary>
    public static void ClearHistory()
    {
        try { ToastNotificationManagerCompat.History.Clear(); } catch { }
    }

    /// <summary>Removes the toast COM/registry registration. Call during uninstall.</summary>
    public static void Uninstall()
    {
        try { ToastNotificationManagerCompat.Uninstall(); } catch { }
    }
}

namespace WgAutoconnect;

/// <summary>
/// Persists "this app connected the VPN" across restarts and upgrades.
/// A marker file in the data directory records ownership of the tunnel;
/// on startup, if the marker exists and the tunnel is up (or still starting),
/// the app resumes managing it instead of treating it as a user-established
/// connection. The uninstaller also uses it to know whether to tear the
/// tunnel down. The marker records whether the connection was a user-forced
/// one so a Force Connect survives restarts without being grace-disconnected.
/// </summary>
public static class ConnectionMarker
{
    private static readonly string MarkerPath =
        Path.Combine(SettingsService.DataDir, "connected-by-app");

    public static bool Exists() => File.Exists(MarkerPath);

    /// <summary>True if the marker records a user-forced connection.</summary>
    public static bool IsForced()
    {
        try
        {
            return Exists() && File.ReadAllText(MarkerPath).StartsWith("forced");
        }
        catch { return false; }
    }

    public static void Set(bool forced = false)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDir);
            File.WriteAllText(MarkerPath,
                $"{(forced ? "forced" : "auto")} {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        }
        catch { }
    }

    public static void Clear()
    {
        try { File.Delete(MarkerPath); } catch { }
    }
}

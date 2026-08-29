using System.Text.Json;

namespace WgAutoconnect;

/// <summary>A selectable tunnel source: display label + the path wireguard.exe consumes.</summary>
public sealed record TunnelChoice(string Display, string Path)
{
    public override string ToString() => Display;
}

public static class SettingsService
{
    public static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WG-Autoconnect");

    private static readonly string SettingsPath = Path.Combine(DataDir, "settings.json");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var loaded = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath), JsonOpts);
                if (loaded != null) return Sanitize(loaded);
            }
        }
        catch (Exception ex)
        {
            // Preserve the corrupt file for the user instead of silently
            // wiping their configuration to defaults.
            try { File.Copy(SettingsPath, SettingsPath + ".bad", overwrite: true); } catch { }
            Logger.Error($"Failed to load settings ({ex.Message}) — backed up to settings.json.bad, using defaults.");
        }
        return new();
    }

    /// <summary>
    /// Clamps hand-edited values into safe ranges so a bad settings.json
    /// can't crash the app at startup (e.g. Timer.Interval must be &gt; 0).
    /// </summary>
    private static AppSettings Sanitize(AppSettings s)
    {
        s.WireGuardConfigPath ??= "";
        s.WireGuardExePath = string.IsNullOrWhiteSpace(s.WireGuardExePath)
            ? @"C:\Program Files\WireGuard\wireguard.exe" : s.WireGuardExePath;
        s.MonitoredApps ??= [];
        s.PollIntervalMs     = Math.Clamp(s.PollIntervalMs,     1000, 600_000);
        s.GracePeriodSeconds = Math.Clamp(s.GracePeriodSeconds,    0,   3600);
        return s;
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDir);
        // Atomic write: a crash mid-write can't corrupt the real settings file.
        var tmp = SettingsPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, JsonOpts));
        File.Move(tmp, SettingsPath, overwrite: true);
    }

    public static List<string> Validate(AppSettings s)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(s.WireGuardConfigPath) || !File.Exists(s.WireGuardConfigPath))
            errors.Add("WireGuard config file (.conf) not found.");
        if (!File.Exists(s.WireGuardExePath))
            errors.Add("WireGuard executable not found.");
        if (s.MonitoredApps.Count == 0)
            errors.Add("Add at least one application to monitor.");
        return errors;
    }

    /// <summary>Scans Program Files for wireguard.exe.</summary>
    public static string? FindWireGuardExe()
    {
        string[] candidates =
        [
            @"C:\Program Files\WireGuard\wireguard.exe",
            @"C:\Program Files (x86)\WireGuard\wireguard.exe",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>Scans Desktop, Downloads, and Documents for .conf files.</summary>
    public static List<string> FindConfFiles()
    {
        var dirs = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };

        var files = new List<string>();
        foreach (var dir in dirs)
        {
            if (!Directory.Exists(dir)) continue;
            try { files.AddRange(Directory.GetFiles(dir, "*.conf", SearchOption.TopDirectoryOnly)); }
            catch { }
        }
        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// Tunnels imported into the WireGuard app itself. WireGuard stores them
    /// as encrypted .conf.dpapi files that wireguard.exe accepts directly for
    /// /installtunnelservice — so users who imported their config into the
    /// WireGuard GUI don't need to keep a loose .conf around.
    /// </summary>
    public static List<TunnelChoice> FindTunnelChoices(string wireGuardExePath)
    {
        var choices = new List<TunnelChoice>();

        try
        {
            var wgDir = Path.GetDirectoryName(wireGuardExePath);
            if (!string.IsNullOrEmpty(wgDir))
            {
                var confDir = Path.Combine(wgDir, "Data", "Configurations");
                if (Directory.Exists(confDir))
                {
                    foreach (var f in Directory.GetFiles(confDir, "*.conf*", SearchOption.TopDirectoryOnly))
                    {
                        if (!f.EndsWith(".conf",       StringComparison.OrdinalIgnoreCase) &&
                            !f.EndsWith(".conf.dpapi", StringComparison.OrdinalIgnoreCase))
                            continue;
                        choices.Add(new TunnelChoice(
                            $"{TunnelNameOf(f)}  (imported in WireGuard)", f));
                    }
                }
            }
        }
        catch { }

        foreach (var f in FindConfFiles())
            choices.Add(new TunnelChoice(f, f));

        return choices;
    }

    private static string TunnelNameOf(string path)
    {
        var name = Path.GetFileName(path);
        if (name.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)) name = name[..^6];
        if (name.EndsWith(".conf",  StringComparison.OrdinalIgnoreCase)) name = name[..^5];
        return name;
    }
}

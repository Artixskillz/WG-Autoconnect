using System.Text.Json;

namespace WgAutoconnect;

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
                return JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(SettingsPath), JsonOpts) ?? new();
        }
        catch { }
        return new();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(DataDir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
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
}

using System.Text.Json.Serialization;

namespace WgAutoconnect;

public class AppSettings
{
    public string       WireGuardConfigPath { get; set; } = "";
    public string       WireGuardExePath    { get; set; } = @"C:\Program Files\WireGuard\wireguard.exe";
    public List<string> MonitoredApps       { get; set; } = [];
    public int          PollIntervalMs      { get; set; } = 5000;
    public int          GracePeriodSeconds  { get; set; } = 10;
    public bool         DisconnectOnExit    { get; set; } = true;

    // Derived from config path — not persisted, always recomputed.
    // Handles both loose .conf files and WireGuard's own imported tunnels
    // (.conf.dpapi in its Data\Configurations folder), matching how
    // wireguard.exe itself derives the tunnel/service name.
    [JsonIgnore]
    public string TunnelName
    {
        get
        {
            var name = Path.GetFileName(WireGuardConfigPath) ?? "";
            if (name.EndsWith(".dpapi", StringComparison.OrdinalIgnoreCase)) name = name[..^6];
            if (name.EndsWith(".conf",  StringComparison.OrdinalIgnoreCase)) name = name[..^5];
            return name;
        }
    }

    [JsonIgnore]
    public string TunnelServiceName => $"WireGuardTunnel${TunnelName}";
}

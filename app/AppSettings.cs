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
    [JsonIgnore]
    public string TunnelName => Path.GetFileNameWithoutExtension(WireGuardConfigPath);

    [JsonIgnore]
    public string TunnelServiceName => $"WireGuardTunnel${TunnelName}";
}

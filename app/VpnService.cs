using System.Diagnostics;
using System.ServiceProcess;

namespace WgAutoconnect;

public class VpnService
{
    private AppSettings _settings;

    public VpnService(AppSettings settings) => _settings = settings;

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    public bool IsConnected()
    {
        try
        {
            using var sc = new ServiceController(_settings.TunnelServiceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (InvalidOperationException) { return false; }
        catch { return false; }
    }

    public async Task ConnectAsync()
        => await RunWireGuard($"/installtunnelservice \"{_settings.WireGuardConfigPath}\"");

    public async Task DisconnectAsync()
        => await RunWireGuard($"/uninstalltunnelservice \"{_settings.TunnelName}\"");

    /// <summary>Polls until connected or timeout. Returns true if connected.</summary>
    public async Task<bool> WaitForConnected(int maxWaitMs = 10_000, int pollMs = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (IsConnected()) return true;
            await Task.Delay(pollMs);
        }
        return false;
    }

    /// <summary>Polls until disconnected or timeout. Returns true if disconnected.</summary>
    public async Task<bool> WaitForDisconnected(int maxWaitMs = 10_000, int pollMs = 500)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(maxWaitMs);
        while (DateTime.UtcNow < deadline)
        {
            if (!IsConnected()) return true;
            await Task.Delay(pollMs);
        }
        return false;
    }

    /// <summary>Synchronous disconnect for use in exit handlers where async is unavailable.</summary>
    public void DisconnectSync()
    {
        try
        {
            var psi = new ProcessStartInfo(_settings.WireGuardExePath,
                $"/uninstalltunnelservice \"{_settings.TunnelName}\"")
            { CreateNoWindow = true, UseShellExecute = false };
            Process.Start(psi)?.WaitForExit(5000);
        }
        catch { }
    }

    private async Task RunWireGuard(string args)
    {
        var psi = new ProcessStartInfo(_settings.WireGuardExePath, args)
        { CreateNoWindow = true, UseShellExecute = false };
        var p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }
}

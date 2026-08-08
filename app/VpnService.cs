using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WgAutoconnect;

public class VpnService
{
    private AppSettings _settings;

    public VpnService(AppSettings settings) => _settings = settings;

    public void UpdateSettings(AppSettings settings) => _settings = settings;

    /// <summary>
    /// Queries the tunnel service state directly via the Service Control Manager.
    /// Called every poll for the lifetime of the app, so it avoids the
    /// ServiceController allocation + thrown-exception-when-absent pattern.
    /// </summary>
    public bool IsConnected()
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenServiceW(scm, _settings.TunnelServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;   // service not installed = disconnected
            try
            {
                return QueryServiceStatus(svc, out var status)
                    && status.dwCurrentState == SERVICE_RUNNING;
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    /// <summary>
    /// True if the tunnel service is installed at all (any state, including
    /// START_PENDING). Distinguishes "still starting" from "absent" so the
    /// startup ownership check doesn't discard the marker during a boot race.
    /// </summary>
    public bool ServiceExists()
    {
        var scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = OpenServiceW(scm, _settings.TunnelServiceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            CloseServiceHandle(svc);
            return true;
        }
        finally { CloseServiceHandle(scm); }
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
            using var p = Process.Start(psi);
            p?.WaitForExit(5000);
        }
        catch { }
    }

    private async Task RunWireGuard(string args)
    {
        var psi = new ProcessStartInfo(_settings.WireGuardExePath, args)
        { CreateNoWindow = true, UseShellExecute = false };
        using var p = Process.Start(psi);
        if (p != null)
        {
            await p.WaitForExitAsync();
            if (p.ExitCode != 0)
                Logger.Warn($"wireguard.exe exited with code {p.ExitCode} (args: {args})");
        }
    }

    // ── Service Control Manager P/Invoke ─────────────────────────

    private const int SC_MANAGER_CONNECT    = 0x0001;
    private const int SERVICE_QUERY_STATUS  = 0x0004;
    private const int SERVICE_RUNNING       = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public int dwServiceType;
        public int dwCurrentState;
        public int dwControlsAccepted;
        public int dwWin32ExitCode;
        public int dwServiceSpecificExitCode;
        public int dwCheckPoint;
        public int dwWaitHint;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machineName, string? databaseName, int access);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr scManager, string serviceName, int access);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr service, out SERVICE_STATUS status);

    [DllImport("advapi32.dll")]
    private static extern bool CloseServiceHandle(IntPtr handle);
}

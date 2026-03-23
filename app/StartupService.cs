using System.Diagnostics;

namespace WgAutoconnect;

public static class StartupService
{
    private const string TaskName = "WG-Autoconnect";

    public static bool IsRegistered()
        => RunSchtasks($"/query /tn \"{TaskName}\"") == 0;

    public static bool Register()
    {
        // Environment.ProcessPath is correct for single-file published executables.
        // Assembly.Location returns empty string or a temp extraction path when published as single-file.
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        return RunSchtasks(
            $"/create /tn \"{TaskName}\" /tr \"\\\"{exe}\\\"\" /sc ONLOGON /rl HIGHEST /f") == 0;
    }

    public static bool Unregister()
        => RunSchtasks($"/delete /tn \"{TaskName}\" /f") == 0;

    private static int RunSchtasks(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks", args)
            { CreateNoWindow = true, UseShellExecute = false };
            var p = Process.Start(psi);
            p?.WaitForExit();
            return p?.ExitCode ?? -1;
        }
        catch { return -1; }
    }
}

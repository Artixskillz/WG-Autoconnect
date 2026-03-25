using System.Diagnostics;

namespace WgAutoconnect;

public static class StartupService
{
    private const string TaskName = "WG-Autoconnect";

    public static bool IsRegistered()
        => RunSchtasks($"/query /tn \"{TaskName}\"", out _) == 0;

    public static bool Register()
    {
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        Logger.Info($"Registering startup task for: {exe}");

        // schtasks wants the /tr value as a single quoted path
        int code = RunSchtasks(
            $"/create /tn \"{TaskName}\" /tr \"'{exe}'\" /sc ONLOGON /rl HIGHEST /f",
            out string output);

        if (code != 0)
            Logger.Error($"schtasks /create failed (exit {code}): {output}");
        else
            Logger.Info("Startup task registered successfully.");

        return code == 0;
    }

    public static bool Unregister()
    {
        int code = RunSchtasks($"/delete /tn \"{TaskName}\" /f", out string output);
        if (code != 0)
            Logger.Error($"schtasks /delete failed (exit {code}): {output}");
        else
            Logger.Info("Startup task removed.");
        return code == 0;
    }

    private static int RunSchtasks(string args, out string output)
    {
        output = "";
        try
        {
            var psi = new ProcessStartInfo("schtasks", args)
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            var p = Process.Start(psi);
            if (p == null) { output = "Failed to start schtasks"; return -1; }
            output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            output = ex.Message;
            return -1;
        }
    }
}

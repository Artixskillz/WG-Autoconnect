using System.Diagnostics;

namespace WgAutoconnect;

public static class StartupService
{
    private const string TaskName = "WG-Autoconnect";

    public static bool IsRegistered()
        => RunSchtasks("/query /tn \"WG-Autoconnect\"", out _) == 0;

    public static bool Register()
    {
        var exe = Environment.ProcessPath ?? Application.ExecutablePath;
        Logger.Info($"Registering startup task for: {exe}");

        // Get current user so the task only triggers for them (not all users on multi-user PCs)
        var userId = System.Security.Principal.WindowsIdentity.GetCurrent().Name;

        // Use XML import — schtasks /create /tr can't handle paths with spaces reliably.
        // Includes a 10-second logon delay so network and WireGuard service are ready.
        var xml = $@"<?xml version=""1.0"" encoding=""UTF-16""?>
<Task version=""1.2"" xmlns=""http://schemas.microsoft.com/windows/2004/02/mit/task"">
  <Triggers>
    <LogonTrigger>
      <Enabled>true</Enabled>
      <UserId>{SecurityElement(userId)}</UserId>
      <Delay>PT10S</Delay>
    </LogonTrigger>
  </Triggers>
  <Principals>
    <Principal id=""Author"">
      <UserId>{SecurityElement(userId)}</UserId>
      <LogonType>InteractiveToken</LogonType>
      <RunLevel>HighestAvailable</RunLevel>
    </Principal>
  </Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
    <Enabled>true</Enabled>
  </Settings>
  <Actions>
    <Exec>
      <Command>{SecurityElement(exe)}</Command>
    </Exec>
  </Actions>
</Task>";

        // Write XML to a temp file, import it, then delete
        var xmlPath = Path.Combine(Path.GetTempPath(), "wg-autoconnect-task.xml");
        try
        {
            File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);

            int code = RunSchtasks($"/create /tn \"WG-Autoconnect\" /xml \"{xmlPath}\" /f", out string output);

            if (code != 0)
                Logger.Error($"schtasks /create failed (exit {code}): {output}");
            else
                Logger.Info("Startup task registered successfully.");

            return code == 0;
        }
        catch (Exception ex)
        {
            Logger.Error($"Register failed: {ex.Message}");
            return false;
        }
        finally
        {
            try { File.Delete(xmlPath); } catch { }
        }
    }

    /// <summary>Escape XML special characters.</summary>
    private static string SecurityElement(string value)
        => System.Security.SecurityElement.Escape(value) ?? value;

    public static bool Unregister()
    {
        int code = RunSchtasks("/delete /tn \"WG-Autoconnect\" /f", out string output);
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

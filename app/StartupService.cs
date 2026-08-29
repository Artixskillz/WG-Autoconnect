using System.Diagnostics;

namespace WgAutoconnect;

public static class StartupService
{
    private const string TaskName = "WG-Autoconnect";

    public static bool IsRegistered()
        => RunSchtasks("/query /tn \"WG-Autoconnect\"", out _) == 0;

    /// <summary>The exe the registered startup task launches, or null.</summary>
    public static string? GetRegisteredCommand()
    {
        if (RunSchtasks("/query /tn \"WG-Autoconnect\" /xml", out string output) != 0) return null;
        var m = System.Text.RegularExpressions.Regex.Match(
            output, @"<Command>(.*?)</Command>", System.Text.RegularExpressions.RegexOptions.Singleline);
        if (!m.Success) return null;
        // Unescape the XML entities Register() writes (&amp; last so it can't
        // create new entities mid-unescape)
        var cmd = m.Groups[1].Value.Trim()
            .Replace("&lt;", "<").Replace("&gt;", ">")
            .Replace("&quot;", "\"").Replace("&apos;", "'")
            .Replace("&amp;", "&");
        return cmd.Trim('"');
    }

    /// <summary>
    /// Re-registers the startup task if it points at a different exe than the
    /// one currently running — e.g. a stale portable copy after the user
    /// switched to the installer, or a moved/renamed exe. Without this, login
    /// keeps launching the OLD exe forever.
    /// </summary>
    public static void HealRegistration()
    {
        try
        {
            if (!IsRegistered()) return;
            var registered = GetRegisteredCommand();
            var current    = Environment.ProcessPath;
            if (registered == null || current == null) return;
            if (string.Equals(registered, current, StringComparison.OrdinalIgnoreCase)) return;

            Logger.Info($"Startup task points at '{registered}' — re-registering for '{current}'.");
            if (Register())
                Logger.Info("Startup task healed.");
        }
        catch (Exception ex)
        {
            Logger.Warn($"Startup task heal failed: {ex.Message}");
        }
    }

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

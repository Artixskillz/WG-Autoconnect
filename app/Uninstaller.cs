namespace WgAutoconnect;

public static class Uninstaller
{
    public static void Run()
    {
        var result = MessageBox.Show(
            "This will uninstall WG-Autoconnect:\n\n" +
            "  • Remove startup task from Task Scheduler\n" +
            "  • Delete settings and log files\n\n" +
            "Your WireGuard installation and config files will NOT be affected.\n\n" +
            "Continue?",
            "Uninstall WG-Autoconnect",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (result != DialogResult.Yes) return;

        // 1. Remove startup task
        if (StartupService.IsRegistered())
            StartupService.Unregister();

        // 2. Delete app data (settings + logs)
        try
        {
            if (Directory.Exists(SettingsService.DataDir))
                Directory.Delete(SettingsService.DataDir, recursive: true);
        }
        catch { }

        // 3. Offer to delete the exe itself
        var exePath = Environment.ProcessPath;
        var deleteExe = MessageBox.Show(
            "Uninstall complete!\n\n" +
            "Would you also like to delete the application file?\n" +
            $"({exePath})",
            "Uninstall WG-Autoconnect",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (deleteExe == DialogResult.Yes && exePath != null)
        {
            // Can't delete ourselves while running — schedule deletion via cmd after a short delay
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe",
                $"/c timeout /t 2 /nobreak >nul & del /f /q \"{exePath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            System.Diagnostics.Process.Start(psi);
        }

        MessageBox.Show(
            "WG-Autoconnect has been uninstalled.",
            "Uninstall WG-Autoconnect",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// Silent uninstall — removes startup task and app data without any prompts.
    /// Called by the Inno Setup uninstaller via --uninstall-silent flag.
    /// </summary>
    public static void RunSilent()
    {
        // Remove startup task
        if (StartupService.IsRegistered())
            StartupService.Unregister();

        // Delete app data (settings + logs)
        try
        {
            if (Directory.Exists(SettingsService.DataDir))
                Directory.Delete(SettingsService.DataDir, recursive: true);
        }
        catch { }
    }
}

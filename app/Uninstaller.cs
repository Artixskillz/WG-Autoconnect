namespace WgAutoconnect;

public static class Uninstaller
{
    /// <summary>Shows the uninstall confirmation. Nothing is touched unless the user agrees.</summary>
    public static bool Confirm()
    {
        return MessageBox.Show(
            "This will uninstall WG-Autoconnect:\n\n" +
            "  • Disconnect the VPN tunnel if this app connected it\n" +
            "  • Remove startup task from Task Scheduler\n" +
            "  • Optionally delete settings and logs (you'll be asked)\n\n" +
            "Your WireGuard installation and config files will NOT be affected.\n\n" +
            "Continue?",
            "Uninstall WG-Autoconnect",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    }

    /// <summary>Interactive uninstall: confirm, then remove everything.</summary>
    public static void Run()
    {
        if (!Confirm()) return;
        Execute(interactive: true);
    }

    /// <summary>
    /// Silent uninstall — no prompts. Called by the Inno Setup uninstaller
    /// via the --uninstall-silent flag (--keep-settings preserves app data).
    /// </summary>
    public static void RunSilent(bool keepSettings = false)
        => Execute(interactive: false, liveSettings: null, keepSettings);

    /// <summary>
    /// Performs the actual removal steps. Call Confirm() first for interactive
    /// flows. Pass the running session's settings via liveSettings when
    /// available — the on-disk settings can diverge from what this session
    /// actually manages (rejected hand-edit, rename deferred at shutdown).
    /// </summary>
    public static void Execute(bool interactive, AppSettings? liveSettings = null, bool keepSettings = false)
    {
        // 1. Tear down the tunnel if THIS app connected it (marker file).
        //    Must happen before the data dir (marker + settings) is deleted.
        //    A tunnel the user connected via the WireGuard GUI is left alone.
        TearDownTunnelIfOwned(liveSettings);

        // 2. Remove startup task
        if (StartupService.IsRegistered())
            StartupService.Unregister();

        // 3. Delete app data (settings + logs + marker) — unless the user
        //    keeps it, in which case a future reinstall picks the
        //    configuration up exactly where they left off.
        bool deleteData = !keepSettings;
        if (interactive)
        {
            deleteData = MessageBox.Show(
                "Also delete your settings and logs?\n\n" +
                "Choose No to keep them — if you reinstall later, your\n" +
                "configuration will be picked up automatically.",
                "Uninstall WG-Autoconnect",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }
        if (deleteData)
        {
            try
            {
                if (Directory.Exists(SettingsService.DataDir))
                    Directory.Delete(SettingsService.DataDir, recursive: true);
            }
            catch { }
        }

        if (!interactive) return;

        // 4. Offer to delete the exe itself
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
            "WG-Autoconnect has been uninstalled." +
            (deleteData ? "" : "\n\nYour settings were kept and will be used if you reinstall."),
            "Uninstall WG-Autoconnect",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    /// <summary>
    /// If this app connected the tunnel (marker present), uninstall the tunnel
    /// service so uninstalling the app doesn't leave a permanent unmanaged VPN.
    /// </summary>
    private static void TearDownTunnelIfOwned(AppSettings? liveSettings = null)
    {
        try
        {
            if (!ConnectionMarker.Exists()) return;
            var settings = liveSettings ?? SettingsService.Load();
            if (string.IsNullOrWhiteSpace(settings.TunnelName)) return;

            var vpn = new VpnService(settings);

            // The installer kills the app before running --uninstall-silent; an
            // /installtunnelservice child that survived the kill may still be
            // creating the service. Poll briefly so a not-yet-visible install
            // isn't mistaken for "no tunnel" and orphaned. Bail out early when
            // no wireguard.exe is alive — nothing can still be creating the
            // service, so a stale marker doesn't stall the uninstaller 10s.
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!vpn.ServiceExists() && DateTime.UtcNow < deadline)
            {
                var wg = System.Diagnostics.Process.GetProcessesByName("wireguard");
                bool installerAlive = wg.Length > 0;
                foreach (var p in wg) p.Dispose();
                if (!installerAlive) break;
                Thread.Sleep(500);
            }

            // Gate on service EXISTENCE, not RUNNING — a START_PENDING service
            // must be uninstalled too. Release the marker only once it's gone.
            if (vpn.ServiceExists())
                vpn.DisconnectSync();
            if (!vpn.ServiceExists())
                ConnectionMarker.Clear();
        }
        catch { }
    }
}

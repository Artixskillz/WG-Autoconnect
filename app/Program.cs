namespace WgAutoconnect;

static class Program
{
    // Static field keeps the mutex alive for the entire process lifetime —
    // a local variable would be collected by the GC and release the OS handle.
    private static Mutex? _mutex;

    private const string ShowSettingsEventName = @"Global\WgAutoconnect-ShowSettings";

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Uninstaller.Run();
            return;
        }

        // Silent uninstall (used by Inno Setup uninstaller — no prompts).
        // --keep-settings preserves %AppData% so a reinstall resumes the config.
        if (args.Length > 0 && args[0].Equals("--uninstall-silent", StringComparison.OrdinalIgnoreCase))
        {
            bool keepSettings = args.Any(a => a.Equals("--keep-settings", StringComparison.OrdinalIgnoreCase));
            Uninstaller.RunSilent(keepSettings);
            return;
        }

        // Register startup task and exit (used by Inno Setup installer)
        if (args.Length > 0 && args[0].Equals("--register-startup", StringComparison.OrdinalIgnoreCase))
        {
            StartupService.Register();
            return;
        }

        _mutex = new Mutex(true, "Global\\WgAutoconnect-SingleInstance", out bool isNew);
        if (!isNew)
        {
            // Another instance is running — ask it to open its Settings window
            // instead of showing a dead-end message box.
            try
            {
                using var evt = EventWaitHandle.OpenExisting(ShowSettingsEventName);
                evt.Set();
            }
            catch
            {
                MessageBox.Show(
                    "WG-Autoconnect is already running.\n\nLook for it in your system tray.",
                    "WG-Autoconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            return;
        }

        try
        {
            // Catch unhandled exceptions so the app doesn't silently die
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) =>
            {
                Logger.Error($"Unhandled UI exception: {e.Exception}");
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    Logger.Error($"Unhandled exception: {ex}");
            };

            ApplicationConfiguration.Initialize();

            // Install the WinForms synchronization context BEFORE anything
            // captures SynchronizationContext.Current — it is otherwise null
            // until the first Control is constructed.
            SynchronizationContext.SetSynchronizationContext(
                new WindowsFormsSynchronizationContext());

            // Signal handle the second-instance path uses to surface this instance
            using var showSettingsSignal = new EventWaitHandle(
                false, EventResetMode.AutoReset, ShowSettingsEventName);

            // First-run setup happens HERE, before Application.Run — if the user
            // cancels, we simply return. (Calling Application.Exit() from inside
            // the AppContext constructor did nothing: no message loop was running
            // yet, so Run() would start afterward and leave a headless ghost
            // process holding the single-instance mutex.)
            var settings   = SettingsService.Load();
            bool isFirstRun = SettingsService.Validate(settings).Count > 0;
            if (isFirstRun)
            {
                using var form = new SetupForm(settings);
                if (form.ShowDialog() != DialogResult.OK)
                    return;
                settings = SettingsService.Load();
            }

            Application.Run(new AppContext(settings, isFirstRun, showSettingsSignal));
        }
        finally
        {
            _mutex.ReleaseMutex();
        }
    }
}

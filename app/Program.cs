namespace WgAutoconnect;

static class Program
{
    // Static field keeps the mutex alive for the entire process lifetime —
    // a local variable would be collected by the GC and release the OS handle.
    private static Mutex? _mutex;

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0].Equals("--uninstall", StringComparison.OrdinalIgnoreCase))
        {
            ApplicationConfiguration.Initialize();
            Uninstaller.Run();
            return;
        }

        // Silent uninstall (used by Inno Setup uninstaller — no prompts)
        if (args.Length > 0 && args[0].Equals("--uninstall-silent", StringComparison.OrdinalIgnoreCase))
        {
            Uninstaller.RunSilent();
            return;
        }

        _mutex = new Mutex(true, "Global\\WgAutoconnect-SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "WG-Autoconnect is already running.\n\nLook for it in your system tray.",
                "WG-Autoconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

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
        Application.Run(new AppContext());

        _mutex.ReleaseMutex();
    }
}

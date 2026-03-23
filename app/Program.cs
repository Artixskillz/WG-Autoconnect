namespace WgAutoconnect;

static class Program
{
    // Static field keeps the mutex alive for the entire process lifetime —
    // a local variable would be collected by the GC and release the OS handle.
    private static Mutex? _mutex;

    [STAThread]
    static void Main()
    {
        _mutex = new Mutex(true, "Global\\WgAutoconnect-SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show(
                "WG-Autoconnect is already running.\n\nLook for it in your system tray.",
                "WG-Autoconnect", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new AppContext());

        _mutex.ReleaseMutex();
    }
}

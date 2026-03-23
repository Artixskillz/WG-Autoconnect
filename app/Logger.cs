namespace WgAutoconnect;

public static class Logger
{
    public static readonly string LogPath    = Path.Combine(SettingsService.DataDir, "app.log");
    private static readonly string OldLogPath = LogPath + ".old";
    private const long MaxLogSize = 524_288; // 512 KB

    public static void Info(string msg)  => Write("INFO ", msg);
    public static void Warn(string msg)  => Write("WARN ", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        try
        {
            Directory.CreateDirectory(SettingsService.DataDir);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogSize)
            {
                File.Move(LogPath, OldLogPath, overwrite: true);
                File.AppendAllText(LogPath, "--- Log rotated ---\n");
            }
            File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  [{level}]  {msg}\n");
        }
        catch { }
    }
}

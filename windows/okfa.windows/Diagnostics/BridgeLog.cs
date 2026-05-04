namespace KeyboardBridge.Windows.Diagnostics;

public static class BridgeLog
{
    private static readonly object SyncRoot = new();
    private static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "okfa"
    );
    private static readonly string LogPath = Path.Combine(LogDirectory, "bridge.log");

    public static string CurrentLogPath => LogPath;

    public static void Write(string category, string message)
    {
        var line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] [{category}] {message}{Environment.NewLine}";

        lock (SyncRoot)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(LogPath, line);
            }
            catch
            {
            }
        }
    }

    public static void WriteException(string category, Exception exception)
    {
        Write(category, $"{exception.GetType().Name}: {exception.Message}");
    }
}

using System.Text;

namespace VibeProxy.WinUI.Services;

public static class LogService
{
    private static readonly object LockObj = new();
    private static string? _logPath;

    public static string LogPath => _logPath ?? string.Empty;

    public static void Initialize()
    {
        var baseDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VibeProxy", "logs");
        Directory.CreateDirectory(baseDir);
        _logPath = Path.Combine(baseDir, "app.log");
        File.WriteAllText(_logPath, string.Empty);
        Write("Log initialized");
    }

    public static void Write(string message, Exception? ex = null)
    {
        if (_logPath is null)
        {
            return;
        }

        var sb = new StringBuilder();
        sb.Append('[').Append(DateTimeOffset.Now.ToString("u")).Append("] ").Append(message);
        if (ex is not null)
        {
            sb.AppendLine();
            sb.Append(ex);
        }
        sb.AppendLine();

        lock (LockObj)
        {
            File.AppendAllText(_logPath, sb.ToString());
        }
    }
}

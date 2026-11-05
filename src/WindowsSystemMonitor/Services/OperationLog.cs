namespace WindowsSystemMonitor.Services;

/// <summary>Small local status log; no command output, paths, package IDs, or device details.</summary>
internal static class OperationLog
{
    private static readonly object Sync = new();
    private const int MaxBytes = 32 * 1024;

    public static void Write(string utility, int exitCode)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsSystemMonitor", "Logs");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "operations.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes) File.Delete(path);
                File.AppendAllText(path, $"{DateTimeOffset.Now:O} {Path.GetFileName(utility)} exit={exitCode}\r\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* Logging must never break a user operation. */ }
    }
}

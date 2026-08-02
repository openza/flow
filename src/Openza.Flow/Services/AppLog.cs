using Windows.Storage;

namespace Openza.Flow.Services;

public static class AppLog
{
    private const long MaxLogBytes = 512 * 1024;
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                "Logs");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup.log");
            lock (Sync)
            {
                if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
                {
                    File.WriteAllText(path, string.Empty);
                }

                File.AppendAllText(path, $"[{DateTimeOffset.Now:u}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void Write(Exception exception)
    {
        Write($"Error category: {exception.GetType().Name}.");
    }
}

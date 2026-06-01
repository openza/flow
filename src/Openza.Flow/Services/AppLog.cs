namespace Openza.Flow.Services;

public static class AppLog
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Openza.Flow");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "startup.log");
            lock (Sync)
            {
                File.AppendAllText(path, $"[{DateTimeOffset.Now:u}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public static void Write(Exception exception)
    {
        Write(exception.ToString());
    }
}

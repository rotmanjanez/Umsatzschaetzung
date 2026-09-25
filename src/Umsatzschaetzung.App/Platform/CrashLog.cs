
namespace Umsatzschaetzung.App.Platform;

static class CrashLog
{
    internal static readonly string Path = System.IO.Path.Combine(AppData.Dir, "crash.log");

    public static Action<string, string>? ShowError;

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, "Schwerwiegender Fehler");
        TaskScheduler.UnobservedTaskException += (_, e) => { Report(e.Exception, "Hintergrundfehler"); e.SetObserved(); };
    }

    public static void Report(Exception? ex, string title) => ShowError?.Invoke(Describe(ex), title);

    internal static string Describe(Exception? ex)
    {
        var detail = ex?.ToString() ?? "unbekannter Fehler";
        var written = Write(detail);
        return (ex?.Message ?? detail) + (written ? $"\n\nDetails: {Path}" : "");
    }

    // What it records may name a case, so it lasts only until the next start.
    internal static void Clear()
    {
        try { File.Delete(Path); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }

    internal static bool Write(string detail)
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.AppendAllText(Path, $"{DateTimeOffset.Now:O}\n{detail}\n\n");
            return true;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}

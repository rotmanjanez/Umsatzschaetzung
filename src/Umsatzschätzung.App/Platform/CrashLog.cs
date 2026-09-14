using System.IO;
using System.Windows;

namespace Umsatzschätzung.App.Platform;

static class CrashLog
{
    static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Umsatzschätzung", "crash.log");

    public static void Install()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Report(e.ExceptionObject as Exception, "Schwerwiegender Fehler");
        TaskScheduler.UnobservedTaskException += (_, e) => { Report(e.Exception, "Hintergrundfehler"); e.SetObserved(); };
    }

    public static void Report(Exception? ex, string title)
    {
        var detail = ex?.ToString() ?? "unbekannter Fehler";
        var written = Write(detail);
        var message = (ex?.Message ?? detail) + (written ? $"\n\nDetails: {Path}" : "");
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    static bool Write(string detail)
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

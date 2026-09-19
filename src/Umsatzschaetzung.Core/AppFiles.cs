
namespace Umsatzschaetzung;

// Mitgelieferte Daten liegen neben dem Programm, im macOS-Bundle aber eine Ebene
// weiter unter Contents/Resources: alles andere unter Contents/MacOS hält codesign
// für verschachtelten Code und verweigert die Signatur.
static class AppFiles
{
    public static string Dir { get; } = Resolve();

    public static string Beside(string path) => Path.Combine(Dir, path);

    static string Resolve()
    {
        var app = AppContext.BaseDirectory;
        if (!OperatingSystem.IsMacOS()) return app;
        var resources = Path.GetFullPath(Path.Combine(app, "..", "Resources"));
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(app)) == "MacOS" && Directory.Exists(resources)
            ? resources
            : app;
    }
}

namespace Umsatzschaetzung.App.Platform;

// Zwei Fenster auf einem Speicher sperren sich nicht aus, SQLite verträgt das. Aber
// jedes hält seinen eigenen Stand der Regeln im Kopf und überschreibt am Ende den des
// anderen, ohne es zu merken. Die Sperrdatei liegt beim Speicher, damit auch ein
// zweiter Benutzer auf einer gemeinsamen Freigabe gewarnt wird. Ein harter Abbruch
// hinterlässt allenfalls die Datei, nie die Sperre: die gibt das Betriebssystem frei.
sealed class InstanceLock : IDisposable
{
    const string Name = "instanz.lock";

    readonly FileStream? handle;

    InstanceLock(FileStream? handle, bool held)
    {
        this.handle = handle;
        Held = held;
    }

    public bool Held { get; }

    public static InstanceLock Acquire(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            return new(new FileStream(Path.Combine(dir, Name), FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None, 1, FileOptions.DeleteOnClose), true);
        }
        catch (IOException)
        {
            return new(null, false);
        }
        // Ein Speicher, in dem nicht geschrieben werden darf, sagt nichts über andere
        // Instanzen: dann lieber ohne Wächter starten als grundlos warnen.
        catch (Exception e) when (e is UnauthorizedAccessException or NotSupportedException)
        {
            return new(null, true);
        }
    }

    public void Dispose() => handle?.Dispose();
}

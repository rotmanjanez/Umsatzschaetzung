using System.Text;
using Ausbeute.Model;

namespace Ausbeute.Rulestore;

public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class RulesConflictException(string message) : Exception(message);

public sealed class RuleStore(string dir, string cacheFile)
{
    readonly string storeFile = Path.Combine(dir, "rules.json");

    public bool Online { get; private set; }

    public RuleSet Load()
    {
        byte[] bytes;
        try
        {
            if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);
            bytes = File.Exists(storeFile) ? File.ReadAllBytes(storeFile) : [];
            Online = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Online = false;
            return File.Exists(cacheFile) ? Json.Deserialize<RuleSet>(File.ReadAllBytes(cacheFile)) : new RuleSet();
        }
        if (bytes.Length == 0) return new RuleSet();
        Cache(bytes);
        return Json.Deserialize<RuleSet>(bytes);
    }

    public void Save(RuleSet rs)
    {
        var stored = Load();
        if (!Online) throw new StoreUnavailableException($"Regelspeicher {dir} nicht erreichbar");
        if (stored.Version != rs.Version)
            throw new RulesConflictException($"Regelwerk wurde zwischenzeitlich geändert (Version {stored.Version}, bearbeitet wurde Version {rs.Version})");
        rs.Version++;
        var bytes = Encoding.UTF8.GetBytes(Json.Serialize(rs) + "\n");
        try
        {
            WriteAtomic(storeFile, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            rs.Version--;
            Online = false;
            throw new StoreUnavailableException($"Regelwerk speichern: {e.Message}", e);
        }
        Cache(bytes);
    }

    void Cache(byte[] bytes)
    {
        try
        {
            if (File.Exists(cacheFile) && File.ReadAllBytes(cacheFile).AsSpan().SequenceEqual(bytes)) return;
            WriteAtomic(cacheFile, bytes);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    static void WriteAtomic(string path, byte[] data)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(parent);
        var tmp = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                f.Write(data);
                f.Flush(true);
            }
            File.Move(tmp, path, true);
        }
        catch
        {
            File.Delete(tmp);
            throw;
        }
    }
}

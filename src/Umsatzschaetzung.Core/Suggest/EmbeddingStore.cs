using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung.Suggest;

// Derived from rule texts and the model, never edited, and the same for everyone: the one
// warmed over the shipped rules comes with the program and is only read, and what is embedded
// since goes into the one in `dir`, which is the user's own and can be deleted at any time.
// Neither lives beside rules.db, where every installation sharing the rules would write it
// over the share at once.
public sealed class EmbeddingStore : IEmbeddingCache
{
    public const string Shipped = "models/embeddings.db";

    readonly string connectionString;
    readonly string? shipped;

    public EmbeddingStore(string dir, string? shipped = null)
    {
        Directory.CreateDirectory(dir);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dir, "embeddings.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10,
        }.ToString();
        if (shipped is not null)
        {
            this.shipped = new SqliteConnectionStringBuilder { DataSource = shipped, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString();
            Open(this.shipped).Dispose();
        }
        using var db = Open(connectionString);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS embedding(text TEXT NOT NULL, model TEXT NOT NULL, vec BLOB NOT NULL, PRIMARY KEY(text, model))";
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts)
    {
        var found = new Dictionary<string, float[]>(StringComparer.Ordinal);
        Find(connectionString, model, texts, found);
        if (shipped is not null && found.Count < texts.Count)
            Find(shipped, model, texts.Where(t => !found.ContainsKey(t)).ToList(), found);
        return found;
    }

    static void Find(string at, string model, IReadOnlyCollection<string> texts, Dictionary<string, float[]> found)
    {
        if (texts.Count == 0) return;
        using var db = Open(at);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT vec FROM embedding WHERE model = @model AND text = @text";
        cmd.Parameters.AddWithValue("@model", model);
        var text = cmd.Parameters.AddWithValue("@text", "");
        foreach (var t in texts)
        {
            text.Value = t;
            if (cmd.ExecuteScalar() is byte[] { Length: IEncoder.Width * 4 } blob)
                found[t] = MemoryMarshal.Cast<byte, float>(blob).ToArray();
        }
    }

    public void Write(string model, IReadOnlyList<(string Text, float[] Vec)> rows)
    {
        if (rows.Count == 0) return;
        using var db = Open(connectionString);
        using var tx = db.BeginTransaction(deferred: false);
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO embedding(text, model, vec) VALUES(@text, @model, @vec)";
        cmd.Parameters.AddWithValue("@model", model);
        var text = cmd.Parameters.AddWithValue("@text", "");
        var vec = cmd.Parameters.AddWithValue("@vec", Array.Empty<byte>());
        foreach (var (t, v) in rows)
        {
            text.Value = t;
            vec.Value = MemoryMarshal.AsBytes(v.AsSpan()).ToArray();
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    static SqliteConnection Open(string at)
    {
        var db = new SqliteConnection(at);
        db.Open();
        return db;
    }
}

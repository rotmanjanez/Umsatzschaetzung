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

    // Given the model, the vectors of every other one are dropped, as no one reads them again.
    public EmbeddingStore(string dir, string? shipped = null, string? model = null)
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
        if (model is null) return;
        cmd.CommandText = "DELETE FROM embedding WHERE model <> @model";
        cmd.Parameters.AddWithValue("@model", model);
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

    // An index asks for thousands at once.
    static void Find(string at, string model, IReadOnlyCollection<string> texts, Dictionary<string, float[]> found)
    {
        if (texts.Count == 0) return;
        using var db = Open(at);
        foreach (var batch in texts.Chunk(500))
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT text, vec FROM embedding WHERE model = @model AND text IN ({string.Join(',', batch.Select((_, i) => "@t" + i))})";
            cmd.Parameters.AddWithValue("@model", model);
            for (var i = 0; i < batch.Length; i++) cmd.Parameters.AddWithValue("@t" + i, batch[i]);
            using var rows = cmd.ExecuteReader();
            while (rows.Read())
                if (rows.GetValue(1) is byte[] { Length: IEncoder.Width * 4 } blob)
                    found[rows.GetString(0)] = MemoryMarshal.Cast<byte, float>(blob).ToArray();
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

using System.Runtime.InteropServices;
using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung.Suggest;

// Derived from rule texts and the model, never edited: it lives beside rules.db rather
// than in it, so rule snapshots stay small and the file can be deleted at any time.
public sealed class EmbeddingStore : IEmbeddingCache
{
    readonly string connectionString;

    public EmbeddingStore(string dir)
    {
        Directory.CreateDirectory(dir);
        connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dir, "embeddings.db"),
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            DefaultTimeout = 10,
        }.ToString();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "CREATE TABLE IF NOT EXISTS embedding(text TEXT NOT NULL, model TEXT NOT NULL, vec BLOB NOT NULL, PRIMARY KEY(text, model))";
        cmd.ExecuteNonQuery();
    }

    public Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts)
    {
        var found = new Dictionary<string, float[]>(StringComparer.Ordinal);
        if (texts.Count == 0) return found;
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT vec FROM embedding WHERE model = @model AND text = @text";
        cmd.Parameters.AddWithValue("@model", model);
        var text = cmd.Parameters.AddWithValue("@text", "");
        foreach (var t in texts)
        {
            text.Value = t;
            if (cmd.ExecuteScalar() is byte[] { Length: Encoder.Width * 4 } blob)
                found[t] = MemoryMarshal.Cast<byte, float>(blob).ToArray();
        }
        return found;
    }

    public void Write(string model, IReadOnlyList<(string Text, float[] Vec)> rows)
    {
        if (rows.Count == 0) return;
        using var db = Open();
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

    SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }
}

using System.Text.Json;
using Microsoft.Data.Sqlite;
using SkiaSharp;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Headless;

// Where a lesson in the browser starts: `keep` copies the stores as they stand into one folder
// with its zustand.json.
public sealed class Lesson(string store, string cases, string embedded)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // A page a lesson shows reads the same on screen at this quality and weighs half of what the
    // program keeps; the pages are most of what a lesson downloads.
    const int Quality = 60;

    readonly HashSet<string> kept = [];

    // Every wording the guide embeds, whatever the rule store forgets on a restart.
    public EmbeddingStore Embedded { get; } = new(embedded);

    // The stores as SQLite writes them, without the rules' embeddings, which the page fetches warmed
    // with its weights, and without the scans, whose pages are kept rendered beside their reading.
    // What the guide embeds joins them once it has played to its end.
    public void Keep(string to, string? open)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        List<string> files = ["embeddings/embeddings.db"];
        foreach (var (dir, into) in new[] { (store, "rules"), (cases, "cases") })
        {
            Directory.CreateDirectory(Path.Combine(to, into));
            if (!Directory.Exists(dir)) continue;
            foreach (var db in Directory.EnumerateFiles(dir, "*.db").Where(f => Path.GetFileName(f) != "embeddings.db"))
            {
                var file = into + "/" + Path.GetFileName(db);
                Run(db, "VACUUM INTO @copy", Path.Combine(to, file));
                if (into == "cases")
                {
                    Lighten(Path.Combine(to, file));
                    Run(Path.Combine(to, file), "UPDATE document SET data = zeroblob(0) WHERE invoice_id IN (SELECT invoice_id FROM reading_image); VACUUM");
                }
                files.Add(file);
            }
        }
        File.WriteAllText(Path.Combine(to, "zustand.json"), JsonSerializer.Serialize(new { @case = open, files }, Options));
        kept.Add(to);
    }

    // Each lesson finds every wording its steps ask about embedded as the guide embedded it, and
    // every line of its case, so it opens no model in the browser.
    public async Task Finish(IEncoder encoder)
    {
        var names = kept.SelectMany(to => Directory.EnumerateFiles(Path.Combine(to, "cases"), "*.db")).SelectMany(Names)
            .Select(Wares.Normal).Distinct(StringComparer.Ordinal).ToList();
        var known = Embedded.Read(encoder.Model, names);
        var missing = names.Where(n => !known.ContainsKey(n)).ToList();
        if (missing.Count > 0) Embedded.Write(encoder.Model, [.. missing.Zip(await encoder.Embed(missing))]);
        foreach (var to in kept)
        {
            Directory.CreateDirectory(Path.Combine(to, "embeddings"));
            Run(Path.Combine(embedded, "embeddings.db"), "VACUUM INTO @copy", Path.Combine(to, "embeddings", "embeddings.db"));
        }
    }

    static List<string> Names(string db)
    {
        using var connection = Open(db);
        using var read = connection.CreateCommand();
        read.CommandText = "SELECT DISTINCT name FROM invoice_line";
        using var rows = read.ExecuteReader();
        var names = new List<string>();
        while (rows.Read()) names.Add(rows.GetString(0));
        return names;
    }

    static void Lighten(string db)
    {
        using var connection = Open(db);
        var pages = new List<(long Row, byte[] Data)>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT rowid, data FROM reading_image";
            using var rows = read.ExecuteReader();
            while (rows.Read()) pages.Add((rows.GetInt64(0), (byte[])rows.GetValue(1)));
        }
        var lighter = pages.AsParallel().AsOrdered().Select(p =>
        {
            using var page = SKBitmap.Decode(p.Data);
            using var jpeg = page.Encode(SKEncodedImageFormat.Jpeg, Quality);
            return (p.Row, Data: jpeg.ToArray());
        }).ToList();
        using var tx = connection.BeginTransaction();
        using var write = connection.CreateCommand();
        write.CommandText = "UPDATE reading_image SET data = @data WHERE rowid = @row";
        var data = write.Parameters.Add("@data", SqliteType.Blob);
        var row = write.Parameters.Add("@row", SqliteType.Integer);
        foreach (var page in lighter)
        {
            data.Value = page.Data;
            row.Value = page.Row;
            write.ExecuteNonQuery();
        }
        tx.Commit();
    }

    static void Run(string db, string sql, string? copy = null)
    {
        using var connection = Open(db);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (copy is not null) command.Parameters.AddWithValue("@copy", copy);
        command.ExecuteNonQuery();
    }

    static SqliteConnection Open(string db)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}

// Every wording the guide embeds is kept as it was first embedded, so a lesson in the browser finds
// what its steps ask about already embedded and opens no model for it.
public sealed class Kept(IEncoder encoder, IEmbeddingCache cache) : IEncoder
{
    public string Model => encoder.Model;

    public async Task<float[][]> Embed(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = await encoder.Embed(texts, ct);
        var known = cache.Read(encoder.Model, texts);
        cache.Write(encoder.Model, [.. texts.Zip(vectors).Where(t => !known.ContainsKey(t.First)).DistinctBy(t => t.First)]);
        return vectors;
    }

    public Task Load(CancellationToken ct = default) => encoder.Load(ct);

    public int Confidence(double cos) => encoder.Confidence(cos);
}

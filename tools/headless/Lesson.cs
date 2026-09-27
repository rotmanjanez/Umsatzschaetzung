using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung.Headless;

// Where a lesson in the browser starts: `keep` copies the stores as they stand into one folder
// with its zustand.json.
public sealed class Lesson(string store, string cases)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    // The stores as SQLite writes them, without the rules' embeddings, which the page fetches warmed
    // with its weights, and without the scans, whose pages are kept rendered beside their reading.
    public void Keep(string to, string? open)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        List<string> files = [];
        foreach (var (dir, into) in new[] { (store, "rules"), (cases, "cases") })
        {
            Directory.CreateDirectory(Path.Combine(to, into));
            if (!Directory.Exists(dir)) continue;
            foreach (var db in Directory.EnumerateFiles(dir, "*.db").Where(f => Path.GetFileName(f) != "embeddings.db"))
            {
                var file = into + "/" + Path.GetFileName(db);
                Run(db, "VACUUM INTO @copy", Path.Combine(to, file));
                if (into == "cases") Run(Path.Combine(to, file), "UPDATE document SET data = zeroblob(0) WHERE invoice_id IN (SELECT invoice_id FROM reading_image); VACUUM");
                files.Add(file);
            }
        }
        File.WriteAllText(Path.Combine(to, "zustand.json"), JsonSerializer.Serialize(new { @case = open, files }, Options));
    }

    static void Run(string db, string sql, string? copy = null)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        if (copy is not null) command.Parameters.AddWithValue("@copy", copy);
        command.ExecuteNonQuery();
    }
}

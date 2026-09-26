using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace Umsatzschaetzung.Headless;

// Where a lesson in the browser starts and what it will need on the way: `keep` copies the
// stores as they stand, `push` and `diff` note what the ranking answers while the lesson's
// own steps run, all into one folder with its zustand.json.
public sealed class Lesson(string store, string cases)
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public Notes Notes { get; } = new();

    // The stores as SQLite writes them, without the scans, which nothing in a browser can show.
    public void Keep(string to, string? open)
    {
        if (Directory.Exists(to)) Directory.Delete(to, true);
        List<string> files = [];
        foreach (var (dir, into) in new[] { (store, "rules"), (cases, "cases") })
        {
            Directory.CreateDirectory(Path.Combine(to, into));
            foreach (var db in Directory.EnumerateFiles(dir, "*.db").Where(f => Path.GetFileName(f) != "embeddings.db"))
            {
                var file = into + "/" + Path.GetFileName(db);
                Run(db, "VACUUM INTO @copy", Path.Combine(to, file));
                if (into == "cases") Run(Path.Combine(to, file), "UPDATE document SET data = zeroblob(0); VACUUM");
                files.Add(file);
            }
        }
        File.WriteAllText(Path.Combine(to, "zustand.json"), JsonSerializer.Serialize(new { @case = open, files }, Options));
    }

    public void Diff(string to)
    {
        var path = Path.Combine(to, "zustand.json");
        var state = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        state["answers"] = JsonSerializer.SerializeToNode(Notes.Diff(), Options);
        File.WriteAllText(path, state.ToJsonString(Options));
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

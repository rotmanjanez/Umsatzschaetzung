using Microsoft.Data.Sqlite;
using Umsatzschätzung.Model;
using Umsatzschätzung.Richtsatz;

namespace Umsatzschätzung.Rulestore;

public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class RuleStore
{
    const int KeptSnapshots = 10;
    const string SammlungPrefix = "richtsatz/";
    const string Schema = """
        CREATE TABLE IF NOT EXISTS rule(kind TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, deleted_at TEXT, PRIMARY KEY(kind, id)) WITHOUT ROWID;
        CREATE TABLE IF NOT EXISTS sammlung(year INTEGER PRIMARY KEY, klassen INTEGER NOT NULL, quelle TEXT NOT NULL, imported_at TEXT NOT NULL, json TEXT NOT NULL) WITHOUT ROWID;
        CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value INTEGER NOT NULL) WITHOUT ROWID;
        INSERT OR IGNORE INTO meta VALUES('version', 0);
        """;

    readonly string file;
    readonly string snapshotDir;
    readonly string connectionString;

    public string? Notice { get; private set; }

    public RuleStore(string dir, string snapshotDir, RuleSet seed)
    {
        file = Path.Combine(dir, "rules.db");
        this.snapshotDir = snapshotDir;
        connectionString = new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 10 }.ToString();
        Guarded(() =>
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(snapshotDir);
            var existed = File.Exists(file);
            if (existed && !Healthy()) Restore();
            using var db = Open();
            if (existed) Snapshot(db);
            Exec(db, Schema);
            using var tx = db.BeginTransaction(deferred: false);
            foreach (var e in Entities(seed)) Upsert(db, tx, Kind(e), e, "INSERT OR IGNORE INTO rule(kind, id, json) VALUES(@kind, @id, @json)");
            SeedSammlungen(db, tx);
            tx.Commit();
            return 0;
        });
    }

    public static RuleSet Seed()
    {
        using var s = typeof(RuleStore).Assembly.GetManifestResourceStream("seed.json")!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return Json.Deserialize<RuleSet>(m.ToArray());
    }

    public RuleSet Load() => Guarded(() =>
    {
        using var db = Open();
        return Read(db, null);
    });

    public RuleSet Save(IRuleEntity e) => Guarded(() =>
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE meta SET value = value + 1 WHERE key = 'version' RETURNING value";
        e.Meta.Rev = (long)cmd.ExecuteScalar()!;
        Upsert(db, tx, Kind(e), e, "INSERT INTO rule(kind, id, json) VALUES(@kind, @id, @json) "
            + "ON CONFLICT(kind, id) DO UPDATE SET json = excluded.json, deleted_at = NULL");
        var rs = Read(db, tx);
        tx.Commit();
        return rs;
    });

    public RuleSet Delete(Entity kind, string id) => Guarded(() =>
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE meta SET value = value + 1 WHERE key = 'version';"
                + "UPDATE rule SET deleted_at = @now WHERE kind = @kind AND id = @id";
            cmd.Parameters.AddWithValue("@kind", kind.ToString());
            cmd.Parameters.AddWithValue("@id", id);
            cmd.Parameters.AddWithValue("@now", Clock.Now().ToString("O"));
            cmd.ExecuteNonQuery();
        }
        var rs = Read(db, tx);
        tx.Commit();
        return rs;
    });

    public List<SammlungInfo> Sammlungen() => Guarded(() =>
    {
        using var db = Open();
        return Infos(db, null);
    });

    public Sammlung? Sammlung(int year) => Guarded(() =>
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT json FROM sammlung WHERE year = @year";
        cmd.Parameters.AddWithValue("@year", year);
        return cmd.ExecuteScalar() is string json ? Json.Deserialize<Sammlung>(json) : null;
    });

    // Ersetzt eine Sammlung desselben Jahres. Eine gelöschte mitgelieferte Sammlung
    // kommt beim nächsten Start zurück.
    public List<SammlungInfo> ImportSammlung(Sammlung s, string quelle) => Guarded(() =>
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        PutSammlung(db, tx, s, quelle);
        var infos = Infos(db, tx);
        tx.Commit();
        return infos;
    });

    public List<SammlungInfo> DeleteSammlung(int year) => Guarded(() =>
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM sammlung WHERE year = @year";
            cmd.Parameters.AddWithValue("@year", year);
            cmd.ExecuteNonQuery();
        }
        var infos = Infos(db, tx);
        tx.Commit();
        return infos;
    });

    static List<SammlungInfo> Infos(SqliteConnection db, SqliteTransaction? tx)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT year, klassen, quelle, imported_at FROM sammlung ORDER BY year DESC";
        using var r = cmd.ExecuteReader();
        List<SammlungInfo> output = [];
        while (r.Read())
            output.Add(new SammlungInfo(r.GetInt32(0), r.GetInt32(1), r.GetString(2), DateTimeOffset.Parse(r.GetString(3))));
        return output;
    }

    static void PutSammlung(SqliteConnection db, SqliteTransaction tx, Sammlung s, string quelle)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO sammlung(year, klassen, quelle, imported_at, json) VALUES(@year, @klassen, @quelle, @now, @json) "
            + "ON CONFLICT(year) DO UPDATE SET klassen = excluded.klassen, quelle = excluded.quelle, imported_at = excluded.imported_at, json = excluded.json";
        cmd.Parameters.AddWithValue("@year", s.Year);
        cmd.Parameters.AddWithValue("@klassen", s.Klassen.Count);
        cmd.Parameters.AddWithValue("@quelle", quelle);
        cmd.Parameters.AddWithValue("@now", Clock.Now().ToString("O"));
        cmd.Parameters.AddWithValue("@json", Json.Serialize(s));
        cmd.ExecuteNonQuery();
    }

    // Nur die fehlenden Jahrgänge werden gelesen; ein zweiter Start liest keine Ressource mehr.
    static void SeedSammlungen(SqliteConnection db, SqliteTransaction tx)
    {
        HashSet<int> vorhanden = [];
        using (var cmd = db.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT year FROM sammlung";
            using var r = cmd.ExecuteReader();
            while (r.Read()) vorhanden.Add(r.GetInt32(0));
        }
        var asm = typeof(RuleStore).Assembly;
        foreach (var name in asm.GetManifestResourceNames())
        {
            if (!name.StartsWith(SammlungPrefix, StringComparison.Ordinal)) continue;
            if (!int.TryParse(Path.GetFileNameWithoutExtension(name.AsSpan(SammlungPrefix.Length)), out var year) || vorhanden.Contains(year)) continue;
            using var s = asm.GetManifestResourceStream(name)!;
            using var m = new MemoryStream();
            s.CopyTo(m);
            PutSammlung(db, tx, Json.Deserialize<Sammlung>(m.ToArray()), "");
        }
    }

    SqliteConnection Open()
    {
        var db = new SqliteConnection(connectionString);
        db.Open();
        return db;
    }

    bool Healthy()
    {
        try
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA quick_check";
            return cmd.ExecuteScalar() as string == "ok";
        }
        catch (SqliteException e) when (e.SqliteErrorCode is 11 or 26)
        {
            return false;
        }
    }

    void Restore()
    {
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Move(file, file + ".defekt-" + stamp, true);
        File.Delete(file + "-journal");
        var latest = Directory.EnumerateFiles(snapshotDir, "rules-*.db").Order(StringComparer.Ordinal).LastOrDefault();
        if (latest is null)
        {
            Notice = "Regelspeicher war beschädigt und wurde neu angelegt.";
            return;
        }
        File.Copy(latest, file);
        Notice = $"Regelspeicher war beschädigt und wurde aus der Sicherung vom {File.GetLastWriteTime(latest):dd.MM.yyyy HH:mm} wiederhergestellt.";
    }

    void Snapshot(SqliteConnection db)
    {
        var path = Path.Combine(snapshotDir, "rules-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".db");
        var temp = Path.Combine(snapshotDir, "rules-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText = "VACUUM INTO @path";
                cmd.Parameters.AddWithValue("@path", temp);
                cmd.ExecuteNonQuery();
            }
            File.Move(temp, path, true);
            foreach (var old in Directory.EnumerateFiles(snapshotDir, "rules-*.db").OrderDescending(StringComparer.Ordinal).Skip(KeptSnapshots))
                File.Delete(old);
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { }
        }
    }

    static RuleSet Read(SqliteConnection db, SqliteTransaction? tx)
    {
        var rs = new RuleSet();
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM meta WHERE key = 'version'";
        rs.Version = (long)cmd.ExecuteScalar()!;
        cmd.CommandText = "SELECT kind, id, json FROM rule WHERE deleted_at IS NULL";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kind = Enum.Parse<Entity>(r.GetString(0));
            var json = r.GetString(2);
            rs.Put(kind switch
            {
                Entity.Category => Json.Deserialize<Category>(json),
                Entity.Ingredient => Json.Deserialize<Ingredient>(json),
                Entity.Mapping => Json.Deserialize<ArticleMapping>(json),
                Entity.Product => Json.Deserialize<Product>(json),
                _ => Json.Deserialize<YieldRule>(json),
            });
        }
        return rs;
    }

    static void Upsert(SqliteConnection db, SqliteTransaction tx, Entity kind, IRuleEntity e, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.Parameters.AddWithValue("@kind", kind.ToString());
        cmd.Parameters.AddWithValue("@id", e.Id);
        cmd.Parameters.AddWithValue("@json", Encode(e));
        cmd.ExecuteNonQuery();
    }

    static void Exec(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    static IEnumerable<IRuleEntity> Entities(RuleSet rs) =>
        rs.Categories.Values.Concat<IRuleEntity>(rs.Ingredients.Values).Concat(rs.Mappings.Values).Concat(rs.Products.Values).Concat(rs.YieldRules.Values);

    static Entity Kind(IRuleEntity e) => e switch
    {
        Category => Entity.Category,
        Ingredient => Entity.Ingredient,
        ArticleMapping => Entity.Mapping,
        Product => Entity.Product,
        YieldRule => Entity.YieldRule,
        _ => throw new ArgumentException(e.GetType().Name),
    };

    static string Encode(IRuleEntity e) => e switch
    {
        Category x => Json.Serialize(x),
        Ingredient x => Json.Serialize(x),
        ArticleMapping x => Json.Serialize(x),
        Product x => Json.Serialize(x),
        YieldRule x => Json.Serialize(x),
        _ => throw new ArgumentException(e.GetType().Name),
    };

    static T Guarded<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new StoreUnavailableException("Regelspeicher: " + e.Message, e);
        }
    }
}

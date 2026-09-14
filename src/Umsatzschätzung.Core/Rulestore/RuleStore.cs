using Microsoft.Data.Sqlite;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Rulestore;

public sealed class StoreUnavailableException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class RuleStore
{
    const int KeptSnapshots = 10;
    const string Schema = """
        CREATE TABLE IF NOT EXISTS rule(kind TEXT NOT NULL, id TEXT NOT NULL, json TEXT NOT NULL, PRIMARY KEY(kind, id)) WITHOUT ROWID;
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
            foreach (var e in Entities(seed)) Upsert(db, tx, Kind(e), e, "INSERT OR IGNORE INTO rule VALUES(@kind, @id, @json)");
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
        Upsert(db, tx, Kind(e), e, "INSERT INTO rule VALUES(@kind, @id, @json) ON CONFLICT(kind, id) DO UPDATE SET json = excluded.json");
        var rs = Read(db, tx);
        tx.Commit();
        return rs;
    });

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
        cmd.CommandText = "SELECT kind, id, json FROM rule";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var kind = Enum.Parse<Entity>(r.GetString(0));
            var json = r.GetString(2);
            rs.Put(kind switch
            {
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
        rs.Ingredients.Values.Concat<IRuleEntity>(rs.Mappings.Values).Concat(rs.Products.Values).Concat(rs.YieldRules.Values);

    static Entity Kind(IRuleEntity e) => e switch
    {
        Ingredient => Entity.Ingredient,
        ArticleMapping => Entity.Mapping,
        Product => Entity.Product,
        YieldRule => Entity.YieldRule,
        _ => throw new ArgumentException(e.GetType().Name),
    };

    static string Encode(IRuleEntity e) => e switch
    {
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

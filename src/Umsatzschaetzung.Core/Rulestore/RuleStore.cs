using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Rulestore;

public sealed class RuleStore
{
    const int KeptSnapshots = 10;
    const string SammlungPrefix = "richtsatz/";

    // meta.value zählt die Regeländerungen und hat mit der Schemaversion nichts zu tun;
    // die steht in user_version. Gelöschtes bleibt mit deleted_at stehen, damit ein
    // mitgelieferter Satz nicht beim nächsten Start wiederkommt.
    static readonly string[] Migrations = [
        """
        CREATE TABLE meta(key TEXT PRIMARY KEY, value INTEGER NOT NULL) WITHOUT ROWID;
        INSERT INTO meta VALUES('version', 0);

        CREATE TABLE category(
            id TEXT PRIMARY KEY, name TEXT NOT NULL, sparte TEXT,
            valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, rev INTEGER NOT NULL,
            deleted_at TEXT) WITHOUT ROWID;
        CREATE TABLE category_gewerbe(category_id TEXT NOT NULL, ord INTEGER NOT NULL, kennzahl TEXT NOT NULL,
            PRIMARY KEY(category_id, ord)) WITHOUT ROWID;

        CREATE TABLE ingredient(
            id TEXT PRIMARY KEY, name TEXT NOT NULL, category_id TEXT NOT NULL,
            valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, rev INTEGER NOT NULL,
            deleted_at TEXT) WITHOUT ROWID;

        CREATE TABLE mapping(
            id TEXT PRIMARY KEY, supplier_name TEXT, supplier_article_id TEXT, gtin TEXT, name TEXT,
            observed TEXT, unit_code TEXT, ingredient_id TEXT NOT NULL, factor INTEGER, confirmed INTEGER NOT NULL,
            valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, rev INTEGER NOT NULL,
            deleted_at TEXT) WITHOUT ROWID;

        CREATE TABLE product(
            id TEXT PRIMARY KEY, name TEXT NOT NULL,
            valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, rev INTEGER NOT NULL,
            deleted_at TEXT) WITHOUT ROWID;
        CREATE TABLE recipe_line(product_id TEXT NOT NULL, ord INTEGER NOT NULL, ingredient_id TEXT NOT NULL,
            amount INTEGER NOT NULL, unit TEXT NOT NULL, PRIMARY KEY(product_id, ord)) WITHOUT ROWID;

        CREATE TABLE yield_rule(
            id TEXT PRIMARY KEY, name TEXT NOT NULL, category_id TEXT, ingredient_id TEXT,
            shrinkage INTEGER NOT NULL, own_use INTEGER NOT NULL, staff INTEGER NOT NULL, free INTEGER NOT NULL,
            is_default INTEGER NOT NULL,
            valid_from TEXT, valid_to TEXT, changed_at TEXT NOT NULL, rev INTEGER NOT NULL,
            deleted_at TEXT) WITHOUT ROWID;

        CREATE TABLE sammlung(year INTEGER PRIMARY KEY, quelle TEXT NOT NULL, imported_at TEXT NOT NULL) WITHOUT ROWID;
        CREATE TABLE klasse(year INTEGER NOT NULL, ord INTEGER NOT NULL, name TEXT NOT NULL, zusatz TEXT,
            bemerkung TEXT, seite INTEGER NOT NULL, PRIMARY KEY(year, ord)) WITHOUT ROWID;
        CREATE TABLE klasse_kennzahl(year INTEGER NOT NULL, klasse INTEGER NOT NULL, ord INTEGER NOT NULL,
            kennzahl TEXT NOT NULL, PRIMARY KEY(year, klasse, ord)) WITHOUT ROWID;
        CREATE TABLE staffel(year INTEGER NOT NULL, klasse INTEGER NOT NULL, ord INTEGER NOT NULL,
            stufe TEXT, von INTEGER, bis INTEGER, PRIMARY KEY(year, klasse, ord)) WITHOUT ROWID;
        CREATE TABLE satz(year INTEGER NOT NULL, klasse INTEGER NOT NULL, staffel INTEGER NOT NULL,
            art TEXT NOT NULL, von INTEGER, bis INTEGER, mittel INTEGER NOT NULL,
            PRIMARY KEY(year, klasse, staffel, art)) WITHOUT ROWID;
        CREATE TABLE synonym(year INTEGER NOT NULL, ord INTEGER NOT NULL, begriff TEXT NOT NULL, klasse TEXT NOT NULL,
            PRIMARY KEY(year, ord)) WITHOUT ROWID;
        CREATE TABLE pauschbetrag(year INTEGER NOT NULL, ord INTEGER NOT NULL, von TEXT NOT NULL, bis TEXT NOT NULL,
            gewerbezweig TEXT NOT NULL, ermaessigt INTEGER NOT NULL, voll INTEGER NOT NULL, gesamt INTEGER NOT NULL,
            PRIMARY KEY(year, ord)) WITHOUT ROWID;

        CREATE INDEX synonym_begriff ON synonym(begriff);
        CREATE INDEX klasse_kennzahl_wert ON klasse_kennzahl(kennzahl);
        """,
        "ALTER TABLE ingredient ADD COLUMN aliases TEXT",
        """
        UPDATE category SET sparte = 'handelsware' WHERE sparte IS NULL AND id IN (
            'cat.bau.und.installationsmaterial', 'cat.bestattung', 'cat.druck.und.fotografie', 'cat.einzelhandel.technik.und.haushalt',
            'cat.einzelhandel.textilien', 'cat.fahrradwerkstatt', 'cat.fitness.und.sport', 'cat.floristik.und.garten',
            'cat.friseur.und.kosmetik', 'cat.handwerk.und.reparatur', 'cat.kfz.werkstatt', 'cat.kino.und.freizeit',
            'cat.kraftstoffe.und.brennstoffe', 'cat.manufaktur.und.kreativbetrieb', 'cat.optik.und.schmuck', 'cat.reinigung.und.fahrzeugpflege',
            'cat.shisha.bar', 'cat.tabakwaren.und.e.zigaretten', 'cat.tattoo.und.piercing', 'cat.tierbedarf.und.tierpflege')
        """,
    ];

    static readonly string[] SammlungTables =
        ["pauschbetrag", "synonym", "satz", "staffel", "klasse_kennzahl", "klasse", "sammlung"];

    static readonly string[] SatzArten = ["aufschlag", "rohgewinn1", "rohgewinn2", "halbrein", "rein"];

    readonly string file;
    readonly string snapshotDir;
    readonly string connectionString;

    public string Dir { get; }
    public string? Notice { get; private set; }

    public RuleStore(string dir, RuleSet seed)
    {
        Dir = dir;
        file = Path.Combine(dir, "rules.db");
        snapshotDir = Path.Combine(dir, "snapshots");
        connectionString = new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadWriteCreate, Pooling = false, DefaultTimeout = 10 }.ToString();
        Guarded(() =>
        {
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(snapshotDir);
            var existed = File.Exists(file);
            if (existed && !Healthy()) Restore();
            using var db = Open();
            if (existed) Snapshot(db);
            Schema.Migrate(db, Migrations);
            using var tx = db.BeginTransaction(deferred: false);
            SeedRules(db, tx, seed);
            SeedSammlungen(db, tx);
            tx.Commit();
            return 0;
        });
    }

    // The seed names an entity by its key alone and dates the whole file once; what the
    // store keeps per row is filled in here.
    public static RuleSet Seed()
    {
        using var s = typeof(RuleStore).Assembly.GetManifestResourceStream("seed.json")!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        var bytes = m.ToArray();
        var rs = Json.Deserialize<RuleSet>(bytes);
        using var doc = JsonDocument.Parse(bytes);
        var stamp = doc.RootElement.TryGetProperty("changedAt", out var c) ? c.GetDateTimeOffset() : default;
        Complete(rs.Categories, stamp);
        Complete(rs.Ingredients, stamp);
        Complete(rs.Mappings, stamp);
        Complete(rs.Products, stamp);
        Complete(rs.YieldRules, stamp);
        return rs;
    }

    static void Complete<T>(Dictionary<string, T> entities, DateTimeOffset stamp) where T : IRuleEntity
    {
        foreach (var (id, e) in entities)
        {
            if (e.Id == "") e.Id = id;
            if (e.Meta.ChangedAt == default) e.Meta.ChangedAt = stamp;
        }
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
        e.Meta.Rev = Bump(db, tx);
        Put(db, tx, e);
        var rs = Read(db, tx);
        tx.Commit();
        return rs;
    });

    public RuleSet Delete(Entity kind, string id) => Guarded(() =>
    {
        using var db = Open();
        using var tx = db.BeginTransaction(deferred: false);
        Bump(db, tx);
        Exec(db, tx, $"UPDATE {Table(kind)} SET deleted_at = @now WHERE id = @id",
            ("@id", id), ("@now", Stamp(Clock.Now())));
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
        return ReadSammlung(db, null, year);
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
        DropSammlung(db, tx, year);
        var infos = Infos(db, tx);
        tx.Commit();
        return infos;
    });

    static long Bump(SqliteConnection db, SqliteTransaction tx)
    {
        using var cmd = Command(db, tx, "UPDATE meta SET value = value + 1 WHERE key = 'version' RETURNING value");
        return (long)cmd.ExecuteScalar()!;
    }

    static string Table(Entity kind) => kind switch
    {
        Entity.Category => "category",
        Entity.Ingredient => "ingredient",
        Entity.Mapping => "mapping",
        Entity.Product => "product",
        _ => "yield_rule",
    };

    static void Put(SqliteConnection db, SqliteTransaction tx, IRuleEntity e)
    {
        switch (e)
        {
            case Category x:
                Exec(db, tx, "INSERT INTO category(id, name, sparte, valid_from, valid_to, changed_at, rev) "
                    + "VALUES(@id, @name, @sparte, @from, @to, @changed, @rev) "
                    + "ON CONFLICT(id) DO UPDATE SET name = excluded.name, sparte = excluded.sparte, "
                    + "valid_from = excluded.valid_from, "
                    + "valid_to = excluded.valid_to, changed_at = excluded.changed_at, rev = excluded.rev, deleted_at = NULL",
                    Meta(x, ("@name", x.Name), ("@sparte", SparteName(x.Sparte))));
                Exec(db, tx, "DELETE FROM category_gewerbe WHERE category_id = @id", ("@id", x.Id));
                for (var i = 0; i < x.Gewerbe.Count; i++)
                    Exec(db, tx, "INSERT INTO category_gewerbe(category_id, ord, kennzahl) VALUES(@id, @ord, @kennzahl)",
                        ("@id", x.Id), ("@ord", i), ("@kennzahl", x.Gewerbe[i]));
                break;

            case Ingredient x:
                Exec(db, tx, "INSERT INTO ingredient(id, name, category_id, aliases, valid_from, valid_to, changed_at, rev) "
                    + "VALUES(@id, @name, @category, @aliases, @from, @to, @changed, @rev) "
                    + "ON CONFLICT(id) DO UPDATE SET name = excluded.name, category_id = excluded.category_id, "
                    + "aliases = excluded.aliases, "
                    + "valid_from = excluded.valid_from, valid_to = excluded.valid_to, changed_at = excluded.changed_at, "
                    + "rev = excluded.rev, deleted_at = NULL",
                    Meta(x, ("@name", x.Name), ("@category", x.CategoryId),
                        ("@aliases", x.Aliases.Count == 0 ? null : string.Join("\n", x.Aliases))));
                break;

            case ArticleMapping x:
                Exec(db, tx, "INSERT INTO mapping(id, supplier_name, supplier_article_id, gtin, name, observed, "
                    + "unit_code, ingredient_id, factor, confirmed, valid_from, valid_to, changed_at, rev) "
                    + "VALUES(@id, @supplier, @article, @gtin, @name, @observed, @unit, @ingredient, @factor, @confirmed, "
                    + "@from, @to, @changed, @rev) "
                    + "ON CONFLICT(id) DO UPDATE SET supplier_name = excluded.supplier_name, "
                    + "supplier_article_id = excluded.supplier_article_id, gtin = excluded.gtin, name = excluded.name, "
                    + "observed = excluded.observed, unit_code = excluded.unit_code, ingredient_id = excluded.ingredient_id, "
                    + "factor = excluded.factor, confirmed = excluded.confirmed, valid_from = excluded.valid_from, "
                    + "valid_to = excluded.valid_to, changed_at = excluded.changed_at, rev = excluded.rev, deleted_at = NULL",
                    Meta(x, ("@supplier", x.SupplierName), ("@article", x.SupplierArticleId), ("@gtin", x.Gtin),
                        ("@name", x.Name), ("@observed", x.Observed), ("@unit", x.UnitCode),
                        ("@ingredient", x.IngredientId), ("@factor", x.Factor), ("@confirmed", x.Confirmed)));
                break;

            case Product x:
                Exec(db, tx, "INSERT INTO product(id, name, valid_from, valid_to, changed_at, rev) "
                    + "VALUES(@id, @name, @from, @to, @changed, @rev) "
                    + "ON CONFLICT(id) DO UPDATE SET name = excluded.name, valid_from = excluded.valid_from, "
                    + "valid_to = excluded.valid_to, changed_at = excluded.changed_at, rev = excluded.rev, deleted_at = NULL",
                    Meta(x, ("@name", x.Name)));
                Exec(db, tx, "DELETE FROM recipe_line WHERE product_id = @id", ("@id", x.Id));
                for (var i = 0; i < x.Recipe.Count; i++)
                {
                    var l = x.Recipe[i];
                    Exec(db, tx, "INSERT INTO recipe_line(product_id, ord, ingredient_id, amount, unit) "
                        + "VALUES(@id, @ord, @ingredient, @amount, @unit)",
                        ("@id", x.Id), ("@ord", i), ("@ingredient", l.IngredientId), ("@amount", l.Amount), ("@unit", l.Unit));
                }
                break;

            case YieldRule x:
                Exec(db, tx, "INSERT INTO yield_rule(id, name, category_id, ingredient_id, shrinkage, own_use, staff, "
                    + "free, is_default, valid_from, valid_to, changed_at, rev) "
                    + "VALUES(@id, @name, @category, @ingredient, @shrinkage, @own, @staff, @free, @default, "
                    + "@from, @to, @changed, @rev) "
                    + "ON CONFLICT(id) DO UPDATE SET name = excluded.name, category_id = excluded.category_id, "
                    + "ingredient_id = excluded.ingredient_id, shrinkage = excluded.shrinkage, own_use = excluded.own_use, "
                    + "staff = excluded.staff, free = excluded.free, is_default = excluded.is_default, "
                    + "valid_from = excluded.valid_from, valid_to = excluded.valid_to, changed_at = excluded.changed_at, "
                    + "rev = excluded.rev, deleted_at = NULL",
                    Meta(x, ("@name", x.Name), ("@category", x.CategoryId), ("@ingredient", x.IngredientId),
                        ("@shrinkage", x.Shrinkage), ("@own", x.OwnUse), ("@staff", x.Staff), ("@free", x.Free),
                        ("@default", x.Default)));
                break;

            default:
                throw new ArgumentException(e.GetType().Name);
        }
    }

    static (string Name, object? Value)[] Meta(IRuleEntity e, params (string Name, object? Value)[] own) =>
    [
        ("@id", e.Id),
        ("@from", e.Meta.ValidFrom is { } f ? Day(f) : null),
        ("@to", e.Meta.ValidTo is { } t ? Day(t) : null),
        ("@changed", Stamp(e.Meta.ChangedAt)),
        ("@rev", e.Meta.Rev),
        .. own,
    ];

    static Meta ReadMeta(SqliteDataReader r, int i) => new()
    {
        ValidFrom = Date(r, i),
        ValidTo = Date(r, i + 1),
        ChangedAt = When(r, i + 2),
        Rev = r.GetInt64(i + 3),
    };

    static RuleSet Read(SqliteConnection db, SqliteTransaction? tx)
    {
        var rs = new RuleSet { Version = (long)Scalar(db, tx, "SELECT value FROM meta WHERE key = 'version'")! };

        var gewerbe = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        Rows(db, tx, "SELECT category_id, kennzahl FROM category_gewerbe ORDER BY category_id, ord", r =>
        {
            if (!gewerbe.TryGetValue(r.GetString(0), out var list)) gewerbe[r.GetString(0)] = list = [];
            list.Add(r.GetString(1));
        });
        Rows(db, tx, "SELECT id, name, valid_from, valid_to, changed_at, rev, sparte FROM category WHERE deleted_at IS NULL",
            r => rs.Put(new Category
            {
                Id = r.GetString(0), Name = r.GetString(1), Meta = ReadMeta(r, 2),
                Gewerbe = gewerbe.GetValueOrDefault(r.GetString(0), []),
                Sparte = ReadSparte(r, 6),
            }));

        Rows(db, tx, "SELECT id, name, category_id, valid_from, valid_to, changed_at, rev, aliases FROM ingredient WHERE deleted_at IS NULL",
            r => rs.Put(new Ingredient
            {
                Id = r.GetString(0), Name = r.GetString(1), CategoryId = r.GetString(2), Meta = ReadMeta(r, 3),
                Aliases = [.. (Str(r, 7) ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries)],
            }));

        Rows(db, tx, "SELECT id, supplier_name, supplier_article_id, gtin, name, observed, unit_code, ingredient_id, "
            + "factor, confirmed, valid_from, valid_to, changed_at, rev FROM mapping WHERE deleted_at IS NULL",
            r => rs.Put(new ArticleMapping
            {
                Id = r.GetString(0), SupplierName = Str(r, 1), SupplierArticleId = Str(r, 2), Gtin = Str(r, 3),
                Name = Str(r, 4), Observed = Str(r, 5), UnitCode = Str(r, 6), IngredientId = r.GetString(7),
                Factor = Num(r, 8), Confirmed = r.GetBoolean(9), Meta = ReadMeta(r, 10),
            }));

        var recipes = new Dictionary<string, List<RecipeLine>>(StringComparer.Ordinal);
        Rows(db, tx, "SELECT product_id, ingredient_id, amount, unit FROM recipe_line ORDER BY product_id, ord", r =>
        {
            if (!recipes.TryGetValue(r.GetString(0), out var list)) recipes[r.GetString(0)] = list = [];
            list.Add(new RecipeLine { IngredientId = r.GetString(1), Amount = r.GetInt64(2), Unit = r.GetString(3) });
        });
        Rows(db, tx, "SELECT id, name, valid_from, valid_to, changed_at, rev FROM product WHERE deleted_at IS NULL",
            r => rs.Put(new Product
            {
                Id = r.GetString(0), Name = r.GetString(1), Meta = ReadMeta(r, 2),
                Recipe = recipes.GetValueOrDefault(r.GetString(0), []),
            }));

        Rows(db, tx, "SELECT id, name, category_id, ingredient_id, shrinkage, own_use, staff, free, is_default, "
            + "valid_from, valid_to, changed_at, rev FROM yield_rule WHERE deleted_at IS NULL",
            r => rs.Put(new YieldRule
            {
                Id = r.GetString(0), Name = r.GetString(1), CategoryId = Str(r, 2), IngredientId = Str(r, 3),
                Shrinkage = r.GetInt64(4), OwnUse = r.GetInt64(5), Staff = r.GetInt64(6), Free = r.GetInt64(7),
                Default = r.GetBoolean(8), Meta = ReadMeta(r, 9),
            }));

        return rs;
    }

    static string? SparteName(Sparte s) => s switch
    {
        Sparte.Getränke => "getraenke",
        Sparte.Speisen => "speisen",
        Sparte.Handelsware => "handelsware",
        _ => null,
    };

    static Sparte ReadSparte(SqliteDataReader r, int i) => r.IsDBNull(i) ? Sparte.Unbestimmt : r.GetString(i) switch
    {
        "getraenke" => Sparte.Getränke,
        "speisen" => Sparte.Speisen,
        "handelsware" => Sparte.Handelsware,
        _ => Sparte.Unbestimmt,
    };

    static void SeedRules(SqliteConnection db, SqliteTransaction tx, RuleSet seed)
    {
        foreach (var e in Entities(seed))
        {
            if (Scalar(db, tx, $"SELECT 1 FROM {Table(Kind(e))} WHERE id = @id", ("@id", e.Id)) is not null) continue;
            Put(db, tx, e);
        }
    }

    static List<SammlungInfo> Infos(SqliteConnection db, SqliteTransaction? tx)
    {
        List<SammlungInfo> output = [];
        Rows(db, tx, "SELECT s.year, (SELECT count(*) FROM klasse k WHERE k.year = s.year), s.quelle, s.imported_at "
            + "FROM sammlung s ORDER BY s.year DESC",
            r => output.Add(new SammlungInfo(r.GetInt32(0), r.GetInt32(1), r.GetString(2), When(r, 3))));
        return output;
    }

    static void DropSammlung(SqliteConnection db, SqliteTransaction tx, int year)
    {
        foreach (var t in SammlungTables) Exec(db, tx, $"DELETE FROM {t} WHERE year = @year", ("@year", year));
    }

    static void PutSammlung(SqliteConnection db, SqliteTransaction tx, Sammlung s, string quelle)
    {
        DropSammlung(db, tx, s.Year);
        Exec(db, tx, "INSERT INTO sammlung(year, quelle, imported_at) VALUES(@year, @quelle, @now)",
            ("@year", s.Year), ("@quelle", quelle), ("@now", Stamp(Clock.Now())));

        for (var k = 0; k < s.Klassen.Count; k++)
        {
            var klasse = s.Klassen[k];
            Exec(db, tx, "INSERT INTO klasse(year, ord, name, zusatz, bemerkung, seite) VALUES(@year, @ord, @name, @zusatz, @bemerkung, @seite)",
                ("@year", s.Year), ("@ord", k), ("@name", klasse.Name), ("@zusatz", klasse.Zusatz),
                ("@bemerkung", klasse.Bemerkung), ("@seite", klasse.Seite));

            for (var i = 0; i < klasse.Kennzahlen.Count; i++)
                Exec(db, tx, "INSERT INTO klasse_kennzahl(year, klasse, ord, kennzahl) VALUES(@year, @klasse, @ord, @kennzahl)",
                    ("@year", s.Year), ("@klasse", k), ("@ord", i), ("@kennzahl", klasse.Kennzahlen[i]));

            for (var i = 0; i < klasse.Staffeln.Count; i++)
            {
                var st = klasse.Staffeln[i];
                Exec(db, tx, "INSERT INTO staffel(year, klasse, ord, stufe, von, bis) VALUES(@year, @klasse, @ord, @stufe, @von, @bis)",
                    ("@year", s.Year), ("@klasse", k), ("@ord", i), ("@stufe", st.Stufe), ("@von", st.Von), ("@bis", st.Bis));

                var sätze = new[] { st.Sätze.Aufschlag, st.Sätze.RohgewinnI, st.Sätze.RohgewinnII, st.Sätze.Halbreingewinn, st.Sätze.Reingewinn };
                for (var j = 0; j < sätze.Length; j++)
                {
                    if (sätze[j] is not { } satz) continue;
                    Exec(db, tx, "INSERT INTO satz(year, klasse, staffel, art, von, bis, mittel) VALUES(@year, @klasse, @staffel, @art, @von, @bis, @mittel)",
                        ("@year", s.Year), ("@klasse", k), ("@staffel", i), ("@art", SatzArten[j]),
                        ("@von", satz.Von), ("@bis", satz.Bis), ("@mittel", satz.Mittel));
                }
            }
        }

        for (var i = 0; i < s.Synonyme.Count; i++)
            Exec(db, tx, "INSERT INTO synonym(year, ord, begriff, klasse) VALUES(@year, @ord, @begriff, @klasse)",
                ("@year", s.Year), ("@ord", i), ("@begriff", s.Synonyme[i].Begriff), ("@klasse", s.Synonyme[i].Klasse));

        for (var i = 0; i < s.Pauschbeträge.Count; i++)
        {
            var p = s.Pauschbeträge[i];
            Exec(db, tx, "INSERT INTO pauschbetrag(year, ord, von, bis, gewerbezweig, ermaessigt, voll, gesamt) "
                + "VALUES(@year, @ord, @von, @bis, @zweig, @erm, @voll, @gesamt)",
                ("@year", s.Year), ("@ord", i), ("@von", Day(p.Von)), ("@bis", Day(p.Bis)),
                ("@zweig", p.Gewerbezweig), ("@erm", p.Ermäßigt), ("@voll", p.Voll), ("@gesamt", p.Gesamt));
        }
    }

    static Sammlung? ReadSammlung(SqliteConnection db, SqliteTransaction? tx, int year)
    {
        if (Scalar(db, tx, "SELECT 1 FROM sammlung WHERE year = @year", ("@year", year)) is null) return null;

        var kennzahlen = new Dictionary<int, List<string>>();
        Rows(db, tx, "SELECT klasse, kennzahl FROM klasse_kennzahl WHERE year = @year ORDER BY klasse, ord",
            r =>
            {
                if (!kennzahlen.TryGetValue(r.GetInt32(0), out var list)) kennzahlen[r.GetInt32(0)] = list = [];
                list.Add(r.GetString(1));
            }, ("@year", year));

        var sätze = new Dictionary<(int Klasse, int Staffel), Satz?[]>();
        Rows(db, tx, "SELECT klasse, staffel, art, von, bis, mittel FROM satz WHERE year = @year",
            r =>
            {
                var key = (r.GetInt32(0), r.GetInt32(1));
                if (!sätze.TryGetValue(key, out var arr)) sätze[key] = arr = new Satz?[SatzArten.Length];
                arr[Array.IndexOf(SatzArten, r.GetString(2))] = new Satz(Int(r, 3), Int(r, 4), r.GetInt32(5));
            }, ("@year", year));

        var staffeln = new Dictionary<int, List<Staffel>>();
        Rows(db, tx, "SELECT klasse, ord, stufe, von, bis FROM staffel WHERE year = @year ORDER BY klasse, ord",
            r =>
            {
                var s = sätze.GetValueOrDefault((r.GetInt32(0), r.GetInt32(1))) ?? new Satz?[SatzArten.Length];
                if (!staffeln.TryGetValue(r.GetInt32(0), out var list)) staffeln[r.GetInt32(0)] = list = [];
                list.Add(new Staffel(Str(r, 2), Num(r, 3), Num(r, 4), new Sätze(s[0], s[1], s[2], s[3], s[4])));
            }, ("@year", year));

        List<Klasse> klassen = [];
        Rows(db, tx, "SELECT ord, name, zusatz, bemerkung, seite FROM klasse WHERE year = @year ORDER BY ord",
            r => klassen.Add(new Klasse(
                r.GetString(1), Str(r, 2),
                kennzahlen.GetValueOrDefault(r.GetInt32(0), []),
                staffeln.GetValueOrDefault(r.GetInt32(0), []),
                Str(r, 3), r.GetInt32(4))), ("@year", year));

        List<Synonym> synonyme = [];
        Rows(db, tx, "SELECT begriff, klasse FROM synonym WHERE year = @year ORDER BY ord",
            r => synonyme.Add(new Synonym(r.GetString(0), r.GetString(1))), ("@year", year));

        List<Pauschbetrag> pauschbeträge = [];
        Rows(db, tx, "SELECT von, bis, gewerbezweig, ermaessigt, voll, gesamt FROM pauschbetrag WHERE year = @year ORDER BY ord",
            r => pauschbeträge.Add(new Pauschbetrag(Date(r, 0)!.Value, Date(r, 1)!.Value, r.GetString(2),
                r.GetInt64(3), r.GetInt64(4), r.GetInt64(5))), ("@year", year));

        return new Sammlung(year, klassen, synonyme, pauschbeträge);
    }

    // Nur die fehlenden Jahrgänge werden gelesen; ein zweiter Start liest keine Ressource mehr.
    static void SeedSammlungen(SqliteConnection db, SqliteTransaction tx)
    {
        HashSet<int> vorhanden = [];
        Rows(db, tx, "SELECT year FROM sammlung", r => vorhanden.Add(r.GetInt32(0)));
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
            return Scalar(db, null, "PRAGMA quick_check") as string == "ok";
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
            Notice = "Die Datenbank war beschädigt und wurde neu angelegt.";
            return;
        }
        File.Copy(latest, file);
        Notice = $"Die Datenbank war beschädigt und wurde aus der Sicherung vom {File.GetLastWriteTime(latest):dd.MM.yyyy HH:mm} wiederhergestellt.";
    }

    void Snapshot(SqliteConnection db)
    {
        var path = Path.Combine(snapshotDir, "rules-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".db");
        var temp = Path.Combine(snapshotDir, "rules-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            Exec(db, null, "VACUUM INTO @path", ("@path", temp));
            File.Move(temp, path, true);
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch (IOException) { }
            throw new StoreUnavailableException(
                $"Im Regelspeicher lässt sich keine Sicherung anlegen: {snapshotDir}\n\n{e.Message}\n\n"
                + "Die Gruppe der Anwender braucht in diesem Ordner Lese-, Schreib-, Erstell- und Löschrechte.", e);
        }
        // Zwei gleichzeitig startende Instanzen räumen denselben Ordner auf.
        try
        {
            foreach (var old in Directory.EnumerateFiles(snapshotDir, "rules-*.db").OrderDescending(StringComparer.Ordinal).Skip(KeptSnapshots))
                File.Delete(old);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
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

    static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateTimeOffset d) => d.ToString("O", CultureInfo.InvariantCulture);

    static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    static long? Num(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    static int? Int(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt32(i);

    static DateOnly? Date(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateOnly.ParseExact(r.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateTimeOffset When(SqliteDataReader r, int i) =>
        DateTimeOffset.Parse(r.GetString(i), CultureInfo.InvariantCulture);

    static void Rows(SqliteConnection db, SqliteTransaction? tx, string sql, Action<SqliteDataReader> row,
        params (string Name, object? Value)[] args)
    {
        using var cmd = Command(db, tx, sql, args);
        using var r = cmd.ExecuteReader();
        while (r.Read()) row(r);
    }

    static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    static void Exec(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(db, tx, sql, args);
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(SqliteConnection db, SqliteTransaction? tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(db, tx, sql, args);
        return cmd.ExecuteScalar();
    }

    static T Guarded<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException or SchemaTooNewException)
        {
            throw new StoreUnavailableException("Datenbank: " + e.Message, e);
        }
    }
}

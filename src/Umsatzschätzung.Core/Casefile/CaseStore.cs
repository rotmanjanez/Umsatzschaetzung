using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Casefile;

public sealed class CaseInvalidException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class CaseNotFoundException(string message) : Exception(message);

// Ein Fall ist eine Datei: <id>.db trägt den Fall, jeden Beleg und das, was der Scan
// gelesen wurde, damit ein gespeicherter Beleg zeigen kann, woher seine Werte stammen.
public sealed partial class CaseStore(string dir)
{
    // Der Schritt ist folgenlos, wo seine Tabellen schon stehen: bestehende Falldateien
    // ohne user_version wachsen so in Version 1 hinein.
    static readonly string[] Migrations = [
        """
        CREATE TABLE IF NOT EXISTS kase(id TEXT PRIMARY KEY, json TEXT NOT NULL) WITHOUT ROWID;
        CREATE TABLE IF NOT EXISTS document(invoice_id TEXT PRIMARY KEY, name TEXT, data BLOB, reading BLOB) WITHOUT ROWID;
        """,
    ];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex IdPattern();

    public static bool ValidId(string id) => IdPattern().IsMatch(id) && !id.Contains("..");

    string PathOf(string id) =>
        ValidId(id) ? Path.Combine(dir, id + ".db") : throw new CaseInvalidException($"ungültige Fall-ID \"{id}\"");

    static string InvoiceKey(string caseId, string invoiceId) =>
        ValidId(invoiceId) ? invoiceId : throw new CaseInvalidException($"ungültige ID \"{caseId}\"/\"{invoiceId}\"");

    public List<Case> List() => Guarded<List<Case>>(() =>
    {
        if (!Directory.Exists(dir)) return [];
        var cases = new List<Case>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.db"))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.')) continue;
            try
            {
                using var db = Reader(path);
                cases.Add(Read(db));
            }
            catch (CaseInvalidException e)
            {
                throw new CaseInvalidException($"Fall {name}: {e.Message}", e);
            }
        }
        return cases
            .OrderByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
    });

    public Case Load(string id) => Guarded(() =>
    {
        var path = PathOf(id);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Fall \"{id}\": Fall nicht gefunden");
        Case c;
        try
        {
            using var db = Reader(path);
            c = Read(db);
        }
        catch (CaseInvalidException e)
        {
            throw new CaseInvalidException($"Fall \"{id}\": {e.Message}", e);
        }
        if (c.Id != id) throw new CaseInvalidException($"Fall \"{id}\": Datei enthält Fall \"{c.Id}\"");
        return c;
    });

    public void Save(Case c) => Guarded(() =>
    {
        var json = Encode(c);
        using var db = Writer(c.Id);
        Exec(db, "DELETE FROM kase WHERE id <> @id;"
            + "INSERT INTO kase(id, json) VALUES(@id, @json) ON CONFLICT(id) DO UPDATE SET json = excluded.json",
            ("@id", c.Id), ("@json", json));
        return 0;
    });

    public void Delete(string id) => Guarded(() =>
    {
        var path = PathOf(id);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Fall \"{id}\": Fall nicht gefunden");
        File.Delete(path);
        File.Delete(path + "-journal");
        return 0;
    });

    // Der Fall wandert als eine Datei: die Kopie ist in sich abgeschlossen und trägt die Belege mit.
    public byte[] Export(string id) => Guarded(() =>
    {
        var path = PathOf(id);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Fall \"{id}\": Fall nicht gefunden");
        var temp = Temp();
        try
        {
            using (var db = Reader(path)) Exec(db, "VACUUM INTO @path", ("@path", temp));
            return File.ReadAllBytes(temp);
        }
        finally
        {
            File.Delete(temp);
        }
    });

    public Case Import(byte[] data) => Guarded(() =>
    {
        if (data.Length == 0) throw new CaseInvalidException("Falldatei ist leer");
        Directory.CreateDirectory(dir);
        var temp = Temp();
        try
        {
            File.WriteAllBytes(temp, data);
            Check(temp);
            Case c;
            using (var db = Reader(temp)) c = Read(db);
            File.Move(temp, PathOf(c.Id), true);
            return c;
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
    });

    public void SaveFile(string caseId, string invoiceId, string name, byte[] data) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        name = Path.GetFileName(name);
        if (name == "" || name.StartsWith('.')) throw new CaseInvalidException($"ungültiger Dateiname \"{name}\"");
        using var db = Attached(caseId);
        // Ein Beleg trägt ein Dokument, das alte geht; die Lesung daneben bleibt.
        Exec(db, "INSERT INTO document(invoice_id, name, data) VALUES(@id, @name, @data) "
            + "ON CONFLICT(invoice_id) DO UPDATE SET name = excluded.name, data = excluded.data",
            ("@id", key), ("@name", name), ("@data", data));
        return 0;
    });

    public void DeleteFile(string caseId, string invoiceId) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        if (!File.Exists(PathOf(caseId))) return 0;
        using var db = Writer(caseId);
        Exec(db, "DELETE FROM document WHERE invoice_id = @id", ("@id", key));
        return 0;
    });

    public void SaveReading(string caseId, string invoiceId, byte[] data) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        using var db = Attached(caseId);
        Exec(db, "INSERT INTO document(invoice_id, reading) VALUES(@id, @reading) "
            + "ON CONFLICT(invoice_id) DO UPDATE SET reading = excluded.reading",
            ("@id", key), ("@reading", data));
        return 0;
    });

    public byte[]? LoadReading(string caseId, string invoiceId) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        var path = PathOf(caseId);
        if (!File.Exists(path)) return null;
        using var db = Reader(path);
        return Scalar(db, "SELECT reading FROM document WHERE invoice_id = @id", ("@id", key)) as byte[];
    });

    public (string Name, byte[] Data) LoadFile(string caseId, string invoiceId) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        var path = PathOf(caseId);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Beleg {invoiceId}: Fall nicht gefunden");
        using var db = Reader(path);
        using var cmd = Command(db, "SELECT name, data FROM document WHERE invoice_id = @id", ("@id", key));
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0) || r.IsDBNull(1))
            throw new CaseNotFoundException($"Beleg {invoiceId}: Fall nicht gefunden");
        return (r.GetString(0), (byte[])r.GetValue(1));
    });

    public static string Encode(Case c)
    {
        Defaults(c);
        Validate(c);
        return Json.Serialize(c);
    }

    public static Case Decode(string json)
    {
        Case c;
        try
        {
            c = Json.Deserialize<Case>(json);
        }
        catch (JsonException e)
        {
            throw new CaseInvalidException("Falldatei ungültig: " + e.Message, e);
        }
        Defaults(c);
        Validate(c);
        return c;
    }

    string Temp() => Path.Combine(dir, "." + Guid.NewGuid().ToString("N") + ".tmp");

    SqliteConnection Attached(string caseId)
    {
        if (!File.Exists(PathOf(caseId))) throw new CaseNotFoundException($"Fall \"{caseId}\": Fall nicht gefunden");
        return Writer(caseId);
    }

    SqliteConnection Writer(string id)
    {
        Directory.CreateDirectory(dir);
        return Open(PathOf(id), SqliteOpenMode.ReadWriteCreate);
    }

    static SqliteConnection Reader(string path) => Open(path, SqliteOpenMode.ReadOnly);

    // Eine Falldatei ist ein Dokument und wird beim Öffnen nachgezogen. Die Fassung davor
    // bleibt daneben liegen, falls ein Schritt sich später als falsch herausstellt.
    static SqliteConnection Open(string path, SqliteOpenMode mode)
    {
        var db = Connect(path, mode);
        var from = Schema.Version(db);
        if (from == Migrations.Length) return db;
        db.Dispose();
        if (from > 0 && from < Migrations.Length) File.Copy(path, $"{path}.v{from}.bak", true);
        using (var writer = Connect(path, SqliteOpenMode.ReadWriteCreate))
        {
            try
            {
                Schema.Migrate(writer, Migrations);
            }
            catch (SchemaTooNewException e)
            {
                throw new CaseInvalidException("Falldatei " + e.Message, e);
            }
        }
        return Connect(path, mode);
    }

    static SqliteConnection Connect(string path, SqliteOpenMode mode)
    {
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = mode,
            Pooling = false,
            DefaultTimeout = 10,
        }.ToString());
        db.Open();
        return db;
    }

    // Vor dem Nachziehen, damit eine beschädigte Datei gar nicht erst migriert wird.
    static void Check(string path)
    {
        try
        {
            using var db = Connect(path, SqliteOpenMode.ReadOnly);
            if (Scalar(db, "PRAGMA quick_check") as string != "ok") throw new CaseInvalidException("Falldatei ist beschädigt");
        }
        catch (SqliteException e)
        {
            throw new CaseInvalidException("Falldatei ungültig: " + e.Message, e);
        }
    }

    static Case Read(SqliteConnection db)
    {
        try
        {
            return Scalar(db, "SELECT json FROM kase") is string json
                ? Decode(json)
                : throw new CaseInvalidException("Falldatei enthält keinen Fall");
        }
        catch (SqliteException e)
        {
            throw new CaseInvalidException("Falldatei ungültig: " + e.Message, e);
        }
    }

    static void Defaults(Case c)
    {
        c.Taxpayer ??= new Taxpayer();
        c.Declared ??= [];
        c.Inventory ??= [];
        c.Invoices ??= [];
        c.Products ??= [];
        c.Yields ??= [];
        c.Pinned ??= [];
        foreach (var inv in c.Invoices) inv.Lines ??= [];
    }

    static void Validate(Case c)
    {
        if (!ValidId(c.Id ?? "")) throw new CaseInvalidException($"ungültige Fall-ID \"{c.Id}\"");
        if (string.IsNullOrWhiteSpace(c.Label)) throw new CaseInvalidException("Bezeichnung darf nicht leer sein");
        if (string.IsNullOrWhiteSpace(c.Taxpayer.Name)) throw new CaseInvalidException("Name des Steuerpflichtigen darf nicht leer sein");
        if (string.IsNullOrWhiteSpace(c.Taxpayer.TaxNumber)) throw new CaseInvalidException("Steuernummer darf nicht leer sein");
        if (string.IsNullOrWhiteSpace(c.Taxpayer.PabNumber)) throw new CaseInvalidException("PaB-Nr. darf nicht leer sein");
        if (c.PeriodFrom == default || c.PeriodTo == default)
            throw new CaseInvalidException("Zeitraum muss Daten der Form JJJJ-MM-TT enthalten");
        if (c.PeriodTo < c.PeriodFrom) throw new CaseInvalidException("Zeitraum: Ende liegt vor dem Beginn");
        var vats = new HashSet<long>();
        foreach (var d in c.Declared)
        {
            if (d.Vat is not (0 or 700 or 1900))
                throw new CaseInvalidException($"erklärter Umsatz: unbekannter Steuersatz {Format.Bp(d.Vat)}");
            if (!vats.Add(d.Vat))
                throw new CaseInvalidException($"erklärter Umsatz zu {Format.Bp(d.Vat)} mehrfach angegeben");
            if (d.Net < 0)
                throw new CaseInvalidException($"erklärter Umsatz zu {Format.Bp(d.Vat)} ist negativ");
        }
        var products = new HashSet<string>();
        foreach (var p in c.Products)
        {
            if (string.IsNullOrEmpty(p.ProductId)) throw new CaseInvalidException("Produkt ohne ID");
            if (!products.Add(p.ProductId)) throw new CaseInvalidException($"Produkt \"{p.ProductId}\" mehrfach angegeben");
            if (p.GrossPrice < 0)
                throw new CaseInvalidException($"Produkt \"{p.ProductId}\": Bruttopreis darf nicht negativ sein");
            if (p.Vat is not (0 or 700 or 1900))
                throw new CaseInvalidException($"Produkt \"{p.ProductId}\": Umsatzsteuersatz muss 0, 7 oder 19 % sein");
        }
        foreach (var y in c.Yields)
        {
            if (string.IsNullOrEmpty(y.YieldRuleId)) throw new CaseInvalidException("Ertragsregel-Wahl ohne Regel");
            if (string.IsNullOrEmpty(y.IngredientId) == string.IsNullOrEmpty(y.CategoryId))
                throw new CaseInvalidException($"Ertragsregel-Wahl \"{y.YieldRuleId}\": entweder Zutat oder Kategorie angeben");
        }
        if (c.CreatedAt == default) throw new CaseInvalidException("Erstellungszeitpunkt fehlt");
        if (c.UpdatedAt == default) throw new CaseInvalidException("Änderungszeitpunkt fehlt");
    }

    static SqliteCommand Command(SqliteConnection db, string sql, params (string Name, object Value)[] args)
    {
        var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return cmd;
    }

    static void Exec(SqliteConnection db, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = Command(db, sql, args);
        cmd.ExecuteNonQuery();
    }

    static object? Scalar(SqliteConnection db, string sql, params (string Name, object Value)[] args)
    {
        using var cmd = Command(db, sql, args);
        return cmd.ExecuteScalar();
    }

    static T Guarded<T>(Func<T> body)
    {
        try
        {
            return body();
        }
        catch (Exception e) when (e is SqliteException or IOException or UnauthorizedAccessException)
        {
            throw new StoreUnavailableException("Fallspeicher: " + e.Message, e);
        }
    }
}

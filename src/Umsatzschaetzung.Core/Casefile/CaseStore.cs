using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Casefile;

public sealed class CaseInvalidException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class CaseNotFoundException(string message) : Exception(message);

public sealed class CaseExistsException(string message, string label) : Exception(message)
{
    public string Label { get; } = label;
}

public sealed record Attachment(string InvoiceId, string FileName, byte[] Data, List<OcrPage>? Reading);

// Ein Fall ist eine Datei: <id>.db trägt den Fall, jeden Beleg und das, was der Scan
// gelesen hat, damit ein gespeicherter Beleg zeigen kann, woher seine Werte stammen.
public sealed partial class CaseStore(string dir)
{
    // Wo das Modell einen Schlüssel garantiert -- Validate lässt keinen doppelten Steuersatz
    // und kein doppeltes Produkt durch -- steht er als Primärschlüssel. Wo es das nicht tut,
    // trägt ord die Reihenfolge der Liste, auf die sich die Auswahl der Ertragsregel verlässt.
    static readonly string[] Migrations = [
        """
        CREATE TABLE kase(
            id TEXT PRIMARY KEY, label TEXT NOT NULL,
            period_from TEXT NOT NULL, period_to TEXT NOT NULL,
            name TEXT NOT NULL, tax_number TEXT NOT NULL, pab_number TEXT NOT NULL, gewerbe TEXT NOT NULL,
            created_at TEXT NOT NULL, updated_at TEXT NOT NULL,
            -- Stand der Regeln, gegen den die offenen Positionen zuletzt geprüft wurden.
            mapped_at INTEGER NOT NULL DEFAULT 0) WITHOUT ROWID;
        CREATE TABLE declared(vat INTEGER PRIMARY KEY, ord INTEGER NOT NULL, net INTEGER NOT NULL) WITHOUT ROWID;
        CREATE TABLE inventory(ord INTEGER PRIMARY KEY, ingredient_id TEXT NOT NULL, opening INTEGER NOT NULL, closing INTEGER NOT NULL, unit TEXT NOT NULL);
        CREATE TABLE case_product(product_id TEXT PRIMARY KEY, ord INTEGER NOT NULL, gross_price INTEGER NOT NULL, vat INTEGER NOT NULL,
            recipe_basis INTEGER) WITHOUT ROWID;
        CREATE TABLE yield_choice(ord INTEGER PRIMARY KEY, ingredient_id TEXT, category_id TEXT, yield_rule_id TEXT NOT NULL);
        CREATE TABLE pinned(ord INTEGER PRIMARY KEY, product_id TEXT NOT NULL, portions INTEGER NOT NULL, reason TEXT NOT NULL);
        CREATE TABLE invoice(
            id TEXT PRIMARY KEY, ord INTEGER NOT NULL,
            source TEXT NOT NULL, file_name TEXT NOT NULL, supplier_name TEXT NOT NULL, number TEXT NOT NULL,
            date TEXT, currency TEXT NOT NULL, net_total INTEGER NOT NULL, gross_total INTEGER NOT NULL,
            stated_net INTEGER, stated_gross INTEGER, verified_at TEXT, verified_auto INTEGER) WITHOUT ROWID;
        CREATE TABLE invoice_line(
            invoice_id TEXT NOT NULL, ord INTEGER NOT NULL, no INTEGER NOT NULL, name TEXT NOT NULL,
            seller_article_id TEXT, gtin TEXT, quantity INTEGER NOT NULL, unit_code TEXT NOT NULL,
            unit_price INTEGER NOT NULL, price_base_qty INTEGER NOT NULL, line_net INTEGER NOT NULL,
            vat INTEGER NOT NULL, mapping_id TEXT,
            PRIMARY KEY(invoice_id, ord)) WITHOUT ROWID;
        CREATE TABLE document(invoice_id TEXT PRIMARY KEY, name TEXT, data BLOB) WITHOUT ROWID;

        -- Was der Scan aus dem Beleg geholt hat. Das Seitenbild steht nicht dabei: der Beleg
        -- liegt daneben und wird zum Anzeigen neu gerendert.
        -- scale, skew, turn und settle sagen, wie die Seite vor dem Lesen aufgerichtet wurde:
        -- das neu gerenderte Bild wird ebenso gedreht, damit es wieder unter den Kästen liegt.
        CREATE TABLE reading_page(invoice_id TEXT NOT NULL, ord INTEGER NOT NULL,
            width INTEGER NOT NULL, height INTEGER NOT NULL,
            scale REAL NOT NULL DEFAULT 1, skew REAL NOT NULL DEFAULT 0,
            turn INTEGER NOT NULL DEFAULT 0, settle REAL NOT NULL DEFAULT 0,
            PRIMARY KEY(invoice_id, ord)) WITHOUT ROWID;
        CREATE TABLE reading_word(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, ord INTEGER NOT NULL,
            text TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
            confidence REAL NOT NULL, PRIMARY KEY(invoice_id, page, ord)) WITHOUT ROWID;
        CREATE TABLE reading_header(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, field TEXT NOT NULL,
            text TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL, h INTEGER NOT NULL,
            confidence REAL NOT NULL, PRIMARY KEY(invoice_id, page, field)) WITHOUT ROWID;
        CREATE TABLE reading_line(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, ord INTEGER NOT NULL,
            no INTEGER NOT NULL, name TEXT NOT NULL, seller_article_id TEXT, gtin TEXT, quantity INTEGER NOT NULL,
            unit_code TEXT NOT NULL, unit_price INTEGER NOT NULL, price_base_qty INTEGER NOT NULL,
            line_net INTEGER NOT NULL, vat INTEGER NOT NULL, mapping_id TEXT,
            PRIMARY KEY(invoice_id, page, ord)) WITHOUT ROWID;
        CREATE TABLE reading_cell(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, line INTEGER NOT NULL,
            field TEXT NOT NULL, text TEXT NOT NULL, x INTEGER NOT NULL, y INTEGER NOT NULL, w INTEGER NOT NULL,
            h INTEGER NOT NULL, confidence REAL NOT NULL,
            PRIMARY KEY(invoice_id, page, line, field)) WITHOUT ROWID;
        -- Ein Hinweis hängt an der Seite oder an einer ihrer Zeilen; woran, sagt die Tabelle.
        CREATE TABLE reading_page_flag(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, ord INTEGER NOT NULL,
            code TEXT NOT NULL, message TEXT NOT NULL, line_no INTEGER NOT NULL, field TEXT,
            PRIMARY KEY(invoice_id, page, ord)) WITHOUT ROWID;
        CREATE TABLE reading_line_flag(invoice_id TEXT NOT NULL, page INTEGER NOT NULL, line INTEGER NOT NULL,
            ord INTEGER NOT NULL, code TEXT NOT NULL, message TEXT NOT NULL, line_no INTEGER NOT NULL, field TEXT,
            PRIMARY KEY(invoice_id, page, line, ord)) WITHOUT ROWID;

        -- Rezeptur nur dieser Prüfung. Ein Produkt ohne Zeilen rechnet mit der des Katalogs.
        CREATE TABLE case_recipe(product_id TEXT NOT NULL, ord INTEGER NOT NULL, ingredient_id TEXT NOT NULL,
            amount INTEGER NOT NULL, unit TEXT NOT NULL, PRIMARY KEY(product_id, ord)) WITHOUT ROWID;
        -- Ob eine Ware Umsatz bringt, entscheidet der Betrieb je Zutat.
        CREATE TABLE no_revenue(ingredient_id TEXT PRIMARY KEY) WITHOUT ROWID;
        """,
    ];

    static readonly string[] ReadingTables =
        ["reading_line_flag", "reading_page_flag", "reading_cell", "reading_line", "reading_header", "reading_word", "reading_page"];

    static readonly string[] CaseTables =
        ["kase", "declared", "inventory", "case_product", "case_recipe", "yield_choice", "pinned", "no_revenue", "invoice", "invoice_line"];

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex IdPattern();

    public static bool ValidId(string id) => IdPattern().IsMatch(id) && !id.Contains("..");

    string PathOf(string id) =>
        ValidId(id) ? Path.Combine(dir, id + ".db") : throw new CaseInvalidException($"ungültige Fall-ID \"{id}\"");

    static string InvoiceKey(string caseId, string invoiceId) =>
        ValidId(invoiceId) ? invoiceId : throw new CaseInvalidException($"ungültige ID \"{caseId}\"/\"{invoiceId}\"");

    // A file that is no case hides none of the others; it is named instead.
    public (List<Case> Cases, List<string> Unreadable) List() => Guarded<(List<Case>, List<string>)>(() =>
    {
        if (!Directory.Exists(dir)) return ([], []);
        var cases = new List<Case>();
        var unreadable = new List<string>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.db"))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.')) continue;
            try
            {
                using var db = Reader(path);
                cases.Add(Read(db));
            }
            catch (CaseInvalidException)
            {
                unreadable.Add(name);
            }
        }
        unreadable.Sort(StringComparer.Ordinal);
        return (cases
            .OrderByDescending(s => s.UpdatedAt)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList(), unreadable);
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

    public void Save(Case c, Attachment? add = null) => Guarded(() =>
    {
        Defaults(c);
        Validate(c);
        var key = add is null ? "" : InvoiceKey(c.Id, add.InvoiceId);
        var name = add is { Data.Length: > 0 } ? DocumentName(add.FileName) : "";
        using var db = Writer(c.Id);
        using var tx = db.BeginTransaction(deferred: false);
        foreach (var t in CaseTables) Exec(db, tx, "DELETE FROM " + t);
        Write(db, tx, c);
        if (add is { Data.Length: > 0 }) PutDocument(db, tx, key, name, add.Data);
        if (add?.Reading is { Count: > 0 } pages)
        {
            DropReading(db, tx, key);
            WriteReading(db, tx, key, pages);
        }
        tx.Commit();
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

    // Der Fall wandert als eine Datei: die Kopie ist in sich abgeschlossen und trägt die Belege mit,
    // gelöschte nicht.
    public byte[] Export(string id) => Guarded(() =>
    {
        var path = PathOf(id);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Fall \"{id}\": Fall nicht gefunden");
        var temp = Temp();
        try
        {
            using (var db = Reader(path)) Exec(db, null, "VACUUM INTO @path", ("@path", temp));
            using (var copy = Connect(temp, SqliteOpenMode.ReadWrite))
                if (DropDeleted(copy) > 0) Exec(copy, null, "VACUUM");
            return File.ReadAllBytes(temp);
        }
        finally
        {
            File.Delete(temp);
        }
    });

    public Case Import(byte[] data, bool overwrite) => Guarded(() =>
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
            if (!overwrite && File.Exists(PathOf(c.Id)))
                throw new CaseExistsException($"Fall \"{c.Id}\": Fall ist bereits vorhanden", Load(c.Id).Label);
            File.Move(temp, PathOf(c.Id), true);
            return c;
        }
        catch
        {
            File.Delete(temp);
            throw;
        }
        finally
        {
            // Eine ältere Datei wird beim Lesen nachgezogen; die Sicherung der Zwischenkopie braucht niemand.
            foreach (var bak in Directory.EnumerateFiles(dir, Path.GetFileName(temp) + ".v*.bak")) File.Delete(bak);
        }
    });

    public void SaveFile(string caseId, string invoiceId, string name, byte[] data) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        name = DocumentName(name);
        using var db = Attached(caseId);
        PutDocument(db, null, key, name, data);
        return 0;
    });

    static string DocumentName(string name)
    {
        name = Path.GetFileName(name);
        if (name == "" || name.StartsWith('.')) throw new CaseInvalidException($"ungültiger Dateiname \"{name}\"");
        return name;
    }

    // Ein Beleg trägt ein Dokument, das alte geht; die Lesung daneben bleibt.
    static void PutDocument(SqliteConnection db, SqliteTransaction? tx, string key, string name, byte[] data) =>
        Exec(db, tx, "INSERT INTO document(invoice_id, name, data) VALUES(@id, @name, @data) "
            + "ON CONFLICT(invoice_id) DO UPDATE SET name = excluded.name, data = excluded.data",
            ("@id", key), ("@name", name), ("@data", data));

    // Ein gelöschter Beleg bleibt mit seiner Lesung liegen, bis das Programm das nächste Mal
    // startet: so lange lässt sich das Löschen zurücknehmen. Eine unlesbare Datei hält den Start nicht auf.
    public void Purge() => Guarded(() =>
    {
        if (!Directory.Exists(dir)) return 0;
        foreach (var path in Directory.EnumerateFiles(dir, "*.db"))
        {
            if (Path.GetFileName(path).StartsWith('.')) continue;
            try
            {
                using var db = Open(path, SqliteOpenMode.ReadWrite);
                DropDeleted(db);
            }
            catch (Exception e) when (e is CaseInvalidException or SqliteException) { }
        }
        return 0;
    });

    static int DropDeleted(SqliteConnection db)
    {
        using var tx = db.BeginTransaction(deferred: false);
        var dropped = 0;
        foreach (var t in (string[])["document", .. ReadingTables])
        {
            using var cmd = Command(db, tx, $"DELETE FROM {t} WHERE invoice_id NOT IN (SELECT id FROM invoice)");
            dropped += cmd.ExecuteNonQuery();
        }
        tx.Commit();
        return dropped;
    }

    public void SaveMappedAt(string caseId, long version) => Guarded(() =>
    {
        using var db = Attached(caseId);
        Exec(db, null, "UPDATE kase SET mapped_at = @v", ("@v", version));
        return 0;
    });

    public void SaveReading(string caseId, string invoiceId, List<OcrPage> pages) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        using var db = Attached(caseId);
        using var tx = db.BeginTransaction(deferred: false);
        DropReading(db, tx, key);
        WriteReading(db, tx, key, pages);
        tx.Commit();
        return 0;
    });

    public List<OcrPage>? LoadReading(string caseId, string invoiceId) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        var path = PathOf(caseId);
        if (!File.Exists(path)) return null;
        using var db = Reader(path);
        return ReadReading(db, key);
    });

    public (string Name, byte[] Data) LoadFile(string caseId, string invoiceId) => Guarded(() =>
    {
        var key = InvoiceKey(caseId, invoiceId);
        var path = PathOf(caseId);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Beleg {invoiceId}: Fall nicht gefunden");
        using var db = Reader(path);
        using var cmd = Command(db, null, "SELECT name, data FROM document WHERE invoice_id = @id", ("@id", key));
        using var r = cmd.ExecuteReader();
        if (!r.Read() || r.IsDBNull(0) || r.IsDBNull(1))
            throw new CaseNotFoundException($"Beleg {invoiceId}: Fall nicht gefunden");
        return (r.GetString(0), (byte[])r.GetValue(1));
    });

    static void Write(SqliteConnection db, SqliteTransaction tx, Case c)
    {
        Exec(db, tx, "INSERT INTO kase(id, label, period_from, period_to, name, tax_number, pab_number, gewerbe, created_at, updated_at, mapped_at) "
            + "VALUES(@id, @label, @from, @to, @name, @tax, @pab, @gewerbe, @created, @updated, @mapped)",
            ("@id", c.Id), ("@label", c.Label), ("@from", Day(c.PeriodFrom)), ("@to", Day(c.PeriodTo)),
            ("@name", c.Taxpayer.Name), ("@tax", c.Taxpayer.TaxNumber), ("@pab", c.Taxpayer.PabNumber),
            ("@gewerbe", c.Taxpayer.Gewerbe), ("@created", Stamp(c.CreatedAt)), ("@updated", Stamp(c.UpdatedAt)), ("@mapped", c.MappedAt));

        for (var i = 0; i < c.Declared.Count; i++)
            Exec(db, tx, "INSERT INTO declared(vat, ord, net) VALUES(@vat, @ord, @net)",
                ("@vat", c.Declared[i].Vat), ("@ord", i), ("@net", c.Declared[i].Net));

        for (var i = 0; i < c.Inventory.Count; i++)
        {
            var e = c.Inventory[i];
            Exec(db, tx, "INSERT INTO inventory(ord, ingredient_id, opening, closing, unit) VALUES(@ord, @ing, @opening, @closing, @unit)",
                ("@ord", i), ("@ing", e.IngredientId), ("@opening", e.Opening), ("@closing", e.Closing), ("@unit", e.Unit));
        }

        for (var i = 0; i < c.Products.Count; i++)
        {
            var p = c.Products[i];
            Exec(db, tx, "INSERT INTO case_product(product_id, ord, gross_price, vat, recipe_basis) VALUES(@id, @ord, @price, @vat, @basis)",
                ("@id", p.ProductId), ("@ord", i), ("@price", p.GrossPrice), ("@vat", p.Vat), ("@basis", p.Recipe is null ? null : p.RecipeBasis));
            if (p.Recipe is not { } recipe) continue;
            for (var j = 0; j < recipe.Count; j++)
                Exec(db, tx, "INSERT INTO case_recipe(product_id, ord, ingredient_id, amount, unit) VALUES(@id, @ord, @ing, @amount, @unit)",
                    ("@id", p.ProductId), ("@ord", j), ("@ing", recipe[j].IngredientId), ("@amount", recipe[j].Amount), ("@unit", recipe[j].Unit));
        }

        for (var i = 0; i < c.Yields.Count; i++)
        {
            var y = c.Yields[i];
            Exec(db, tx, "INSERT INTO yield_choice(ord, ingredient_id, category_id, yield_rule_id) VALUES(@ord, @ing, @cat, @rule)",
                ("@ord", i), ("@ing", y.IngredientId), ("@cat", y.CategoryId), ("@rule", y.YieldRuleId));
        }

        for (var i = 0; i < c.Pinned.Count; i++)
        {
            var p = c.Pinned[i];
            Exec(db, tx, "INSERT INTO pinned(ord, product_id, portions, reason) VALUES(@ord, @id, @portions, @reason)",
                ("@ord", i), ("@id", p.ProductId), ("@portions", p.Portions), ("@reason", p.Reason));
        }

        foreach (var id in c.NoRevenue)
            Exec(db, tx, "INSERT OR IGNORE INTO no_revenue(ingredient_id) VALUES(@id)", ("@id", id));

        for (var i = 0; i < c.Invoices.Count; i++)
        {
            var inv = c.Invoices[i];
            Exec(db, tx, "INSERT INTO invoice(id, ord, source, file_name, supplier_name, number, date, currency, "
                + "net_total, gross_total, stated_net, stated_gross, verified_at, verified_auto) "
                + "VALUES(@id, @ord, @source, @file, @supplier, @number, @date, @currency, @net, @gross, @snet, @sgross, @vat, @vauto)",
                ("@id", inv.Id), ("@ord", i), ("@source", inv.Source.ToString()), ("@file", inv.FileName),
                ("@supplier", inv.SupplierName), ("@number", inv.Number), ("@date", inv.Date is { } d ? Day(d) : null),
                ("@currency", inv.Currency), ("@net", inv.NetTotal), ("@gross", inv.GrossTotal),
                ("@snet", inv.StatedNet), ("@sgross", inv.StatedGross),
                ("@vat", inv.Verification is { } v ? Stamp(v.At) : null), ("@vauto", inv.Verification?.Auto));

            for (var j = 0; j < inv.Lines.Count; j++)
            {
                var l = inv.Lines[j];
                Exec(db, tx, $"INSERT INTO invoice_line(invoice_id, ord, {LineColumns}) VALUES(@id, @ord, {LineValues})",
                    LineArgs(l, ("@id", inv.Id), ("@ord", j)));
            }
        }
    }

    static Case Read(SqliteConnection db)
    {
        try
        {
            var c = ReadCase(db);
            ReadRows(db, "SELECT vat, net FROM declared ORDER BY ord",
                r => c.Declared.Add(new DeclaredRevenue { Vat = r.GetInt64(0), Net = r.GetInt64(1) }));
            ReadRows(db, "SELECT ingredient_id, opening, closing, unit FROM inventory ORDER BY ord",
                r => c.Inventory.Add(new InventoryEntry
                {
                    IngredientId = r.GetString(0), Opening = r.GetInt64(1), Closing = r.GetInt64(2), Unit = r.GetString(3),
                }));
            var recipes = ReadRecipes(db);
            ReadRows(db, "SELECT product_id, gross_price, vat, recipe_basis FROM case_product ORDER BY ord",
                r => c.Products.Add(new CaseProduct
                {
                    ProductId = r.GetString(0), GrossPrice = r.GetInt64(1), Vat = r.GetInt64(2),
                    Recipe = recipes.GetValueOrDefault(r.GetString(0)), RecipeBasis = Num(r, 3) ?? 0,
                }));
            ReadRows(db, "SELECT ingredient_id, category_id, yield_rule_id FROM yield_choice ORDER BY ord",
                r => c.Yields.Add(new YieldChoice
                {
                    IngredientId = Str(r, 0), CategoryId = Str(r, 1), YieldRuleId = r.GetString(2),
                }));
            ReadRows(db, "SELECT product_id, portions, reason FROM pinned ORDER BY ord",
                r => c.Pinned.Add(new PinnedPortions
                {
                    ProductId = r.GetString(0), Portions = r.GetInt64(1), Reason = r.GetString(2),
                }));
            ReadRows(db, "SELECT ingredient_id FROM no_revenue ORDER BY ingredient_id", r => c.NoRevenue.Add(r.GetString(0)));

            var lines = ReadLines(db);
            ReadRows(db, "SELECT id, source, file_name, supplier_name, number, date, currency, net_total, gross_total, "
                + "stated_net, stated_gross, verified_at, verified_auto FROM invoice ORDER BY ord",
                r => c.Invoices.Add(new Invoice
                {
                    Id = r.GetString(0),
                    Source = Enum.Parse<Source>(r.GetString(1)),
                    FileName = r.GetString(2),
                    SupplierName = r.GetString(3),
                    Number = r.GetString(4),
                    Date = Date(r, 5),
                    Currency = r.GetString(6),
                    NetTotal = r.GetInt64(7),
                    GrossTotal = r.GetInt64(8),
                    StatedNet = Num(r, 9),
                    StatedGross = Num(r, 10),
                    Verification = r.IsDBNull(11) ? null : new Verification { At = When(r, 11), Auto = r.GetBoolean(12) },
                    Lines = lines.GetValueOrDefault(r.GetString(0), []),
                }));
            Defaults(c);
            Validate(c);
            return c;
        }
        catch (SqliteException e)
        {
            throw new CaseInvalidException("Falldatei ungültig: " + e.Message, e);
        }
    }

    static Case ReadCase(SqliteConnection db)
    {
        using var cmd = Command(db, null, "SELECT id, label, period_from, period_to, name, tax_number, pab_number, "
            + "gewerbe, created_at, updated_at, mapped_at FROM kase");
        using var r = cmd.ExecuteReader();
        if (!r.Read()) throw new CaseInvalidException("Falldatei enthält keinen Fall");
        return new Case
        {
            Id = r.GetString(0),
            Label = r.GetString(1),
            PeriodFrom = Date(r, 2)!.Value,
            PeriodTo = Date(r, 3)!.Value,
            Taxpayer = new Taxpayer
            {
                Name = r.GetString(4), TaxNumber = r.GetString(5), PabNumber = r.GetString(6), Gewerbe = r.GetString(7),
            },
            CreatedAt = When(r, 8),
            UpdatedAt = When(r, 9),
            MappedAt = r.GetInt64(10),
        };
    }

    static Dictionary<string, List<InvoiceLine>> ReadLines(SqliteConnection db)
    {
        var lines = new Dictionary<string, List<InvoiceLine>>(StringComparer.Ordinal);
        ReadRows(db, $"SELECT invoice_id, {LineColumns} FROM invoice_line ORDER BY invoice_id, ord", r =>
        {
            if (!lines.TryGetValue(r.GetString(0), out var list)) lines[r.GetString(0)] = list = [];
            list.Add(ReadLine(r, 1));
        });
        return lines;
    }

    static Dictionary<string, List<RecipeLine>> ReadRecipes(SqliteConnection db)
    {
        var recipes = new Dictionary<string, List<RecipeLine>>(StringComparer.Ordinal);
        ReadRows(db, "SELECT product_id, ingredient_id, amount, unit FROM case_recipe ORDER BY product_id, ord", r =>
        {
            if (!recipes.TryGetValue(r.GetString(0), out var list)) recipes[r.GetString(0)] = list = [];
            list.Add(new RecipeLine { IngredientId = r.GetString(1), Amount = r.GetInt64(2), Unit = r.GetString(3) });
        });
        return recipes;
    }

    static void DropReading(SqliteConnection db, SqliteTransaction tx, string key)
    {
        foreach (var t in ReadingTables) Exec(db, tx, $"DELETE FROM {t} WHERE invoice_id = @id", ("@id", key));
    }

    static void WriteReading(SqliteConnection db, SqliteTransaction tx, string key, List<OcrPage> pages)
    {
        for (var p = 0; p < pages.Count; p++)
        {
            var page = pages[p];
            var c = page.Correction;
            Exec(db, tx, "INSERT INTO reading_page(invoice_id, ord, width, height, scale, skew, turn, settle) "
                + "VALUES(@id, @ord, @width, @height, @scale, @skew, @turn, @settle)",
                ("@id", key), ("@ord", p), ("@width", page.Width), ("@height", page.Height),
                ("@scale", c.Scale), ("@skew", c.Skew), ("@turn", c.Turn), ("@settle", c.Settle));

            for (var i = 0; i < page.Words.Count; i++)
                Exec(db, tx, "INSERT INTO reading_word(invoice_id, page, ord, text, x, y, w, h, confidence) "
                    + "VALUES(@id, @page, @ord, @text, @x, @y, @w, @h, @confidence)",
                    WordArgs(key, page.Words[i], ("@page", p), ("@ord", i)));

            foreach (var (field, word) in page.Header)
                Exec(db, tx, "INSERT INTO reading_header(invoice_id, page, field, text, x, y, w, h, confidence) "
                    + "VALUES(@id, @page, @field, @text, @x, @y, @w, @h, @confidence)",
                    WordArgs(key, word, ("@page", p), ("@field", field.ToString())));

            for (var i = 0; i < page.Flags.Count; i++)
                Exec(db, tx, "INSERT INTO reading_page_flag(invoice_id, page, ord, code, message, line_no, field) "
                    + "VALUES(@id, @page, @ord, @code, @message, @no, @field)",
                    FlagArgs(key, page.Flags[i], ("@page", p), ("@ord", i)));

            for (var i = 0; i < page.Lines.Count; i++)
            {
                var line = page.Lines[i];
                Exec(db, tx, $"INSERT INTO reading_line(invoice_id, page, ord, {LineColumns}) "
                    + $"VALUES(@id, @page, @ord, {LineValues})",
                    LineArgs(line.Parsed, ("@id", key), ("@page", p), ("@ord", i)));

                foreach (var (field, word) in line.Cells)
                    Exec(db, tx, "INSERT INTO reading_cell(invoice_id, page, line, field, text, x, y, w, h, confidence) "
                        + "VALUES(@id, @page, @line, @field, @text, @x, @y, @w, @h, @confidence)",
                        WordArgs(key, word, ("@page", p), ("@line", i), ("@field", field.ToString())));

                for (var j = 0; j < line.Flags.Count; j++)
                    Exec(db, tx, "INSERT INTO reading_line_flag(invoice_id, page, line, ord, code, message, line_no, field) "
                        + "VALUES(@id, @page, @line, @ord, @code, @message, @no, @field)",
                        FlagArgs(key, line.Flags[j], ("@page", p), ("@line", i), ("@ord", j)));
            }
        }
    }

    static List<OcrPage>? ReadReading(SqliteConnection db, string key)
    {
        List<OcrPage> pages = [];
        ReadRows(db, "SELECT width, height, scale, skew, turn, settle FROM reading_page WHERE invoice_id = @id ORDER BY ord",
            r => pages.Add(new OcrPage
            {
                Width = r.GetInt32(0),
                Height = r.GetInt32(1),
                Correction = new Correction { Scale = r.GetDouble(2), Skew = r.GetDouble(3), Turn = r.GetInt32(4), Settle = r.GetDouble(5) },
            }), ("@id", key));
        if (pages.Count == 0) return null;

        ReadRows(db, "SELECT page, text, x, y, w, h, confidence FROM reading_word WHERE invoice_id = @id ORDER BY page, ord",
            r => pages[r.GetInt32(0)].Words.Add(ReadWord(r, 1)), ("@id", key));

        ReadRows(db, "SELECT page, field, text, x, y, w, h, confidence FROM reading_header WHERE invoice_id = @id",
            r => pages[r.GetInt32(0)].Header[Enum.Parse<Field>(r.GetString(1))] = ReadWord(r, 2), ("@id", key));

        ReadRows(db, "SELECT page, code, message, line_no, field FROM reading_page_flag WHERE invoice_id = @id ORDER BY page, ord",
            r => pages[r.GetInt32(0)].Flags.Add(ReadFlag(r, 1)), ("@id", key));

        ReadRows(db, $"SELECT page, {LineColumns} FROM reading_line WHERE invoice_id = @id ORDER BY page, ord",
            r => pages[r.GetInt32(0)].Lines.Add(new OcrLine { Parsed = ReadLine(r, 1) }), ("@id", key));

        ReadRows(db, "SELECT page, line, field, text, x, y, w, h, confidence FROM reading_cell WHERE invoice_id = @id",
            r => pages[r.GetInt32(0)].Lines[r.GetInt32(1)].Cells[Enum.Parse<Field>(r.GetString(2))] = ReadWord(r, 3), ("@id", key));

        ReadRows(db, "SELECT page, line, code, message, line_no, field FROM reading_line_flag WHERE invoice_id = @id ORDER BY page, line, ord",
            r => pages[r.GetInt32(0)].Lines[r.GetInt32(1)].Flags.Add(ReadFlag(r, 2)), ("@id", key));

        return pages;
    }

    // Eine Belegzeile steht zweimal: wie sie im Fall gespeichert ist und wie der Scan sie gelesen hat.
    const string LineColumns = "no, name, seller_article_id, gtin, quantity, unit_code, unit_price, "
        + "price_base_qty, line_net, vat, mapping_id";

    const string LineValues = "@no, @name, @article, @gtin, @qty, @unit, @price, @base, @net, @vat, @mapping";

    static (string Name, object? Value)[] LineArgs(InvoiceLine l, params (string Name, object? Value)[] own) =>
    [
        ("@no", l.No), ("@name", l.Name), ("@article", l.SellerArticleId), ("@gtin", l.Gtin),
        ("@qty", l.Quantity), ("@unit", l.UnitCode), ("@price", l.UnitPrice), ("@base", l.PriceBaseQty),
        ("@net", l.LineNet), ("@vat", l.Vat), ("@mapping", l.MappingId),
        .. own,
    ];

    static InvoiceLine ReadLine(SqliteDataReader r, int i) => new()
    {
        No = r.GetInt64(i),
        Name = r.GetString(i + 1),
        SellerArticleId = Str(r, i + 2),
        Gtin = Str(r, i + 3),
        Quantity = r.GetInt64(i + 4),
        UnitCode = r.GetString(i + 5),
        UnitPrice = r.GetInt64(i + 6),
        PriceBaseQty = r.GetInt64(i + 7),
        LineNet = r.GetInt64(i + 8),
        Vat = r.GetInt64(i + 9),
        MappingId = Str(r, i + 10),
    };

    static (string Name, object? Value)[] WordArgs(string key, OcrWord w, params (string Name, object? Value)[] own) =>
    [
        ("@id", key), ("@text", w.Text), ("@x", w.Box.X), ("@y", w.Box.Y), ("@w", w.Box.W), ("@h", w.Box.H),
        ("@confidence", w.Confidence),
        .. own,
    ];

    static OcrWord ReadWord(SqliteDataReader r, int i) => new()
    {
        Text = r.GetString(i),
        Box = new Box(r.GetInt32(i + 1), r.GetInt32(i + 2), r.GetInt32(i + 3), r.GetInt32(i + 4)),
        Confidence = r.GetFloat(i + 5),
    };

    static (string Name, object? Value)[] FlagArgs(string key, Flag f, params (string Name, object? Value)[] own) =>
    [
        ("@id", key), ("@code", f.Code), ("@message", f.Message), ("@no", f.LineNo),
        ("@field", f.Field?.ToString()),
        .. own,
    ];

    static Flag ReadFlag(SqliteDataReader r, int i) => new()
    {
        Code = r.GetString(i),
        Message = r.GetString(i + 1),
        LineNo = r.GetInt64(i + 2),
        Field = r.IsDBNull(i + 3) ? null : Enum.Parse<Field>(r.GetString(i + 3)),
    };

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
        int from;
        try
        {
            from = Schema.Version(db);
        }
        catch (SqliteException e)
        {
            db.Dispose();
            if (e.SqliteErrorCode is 11 or 26) throw new CaseInvalidException("Falldatei ungültig: " + e.Message, e);
            throw;
        }
        if (from == Migrations.Length) return db;
        db.Dispose();
        if (from == 0 && mode == SqliteOpenMode.ReadOnly) throw new CaseInvalidException("Falldatei enthält keinen Fall");
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

    static void Defaults(Case c)
    {
        c.Taxpayer ??= new Taxpayer();
        c.Declared ??= [];
        c.Inventory ??= [];
        c.Invoices ??= [];
        c.Products ??= [];
        c.Yields ??= [];
        c.Pinned ??= [];
        c.NoRevenue ??= [];
        foreach (var inv in c.Invoices) inv.Lines ??= [];
    }

    static void Validate(Case c)
    {
        if (!ValidId(c.Id ?? "")) throw new CaseInvalidException($"ungültige Fall-ID \"{c.Id}\"");
        if (string.IsNullOrWhiteSpace(c.Label)) throw new CaseInvalidException("Bezeichnung darf nicht leer sein");
        if (string.IsNullOrWhiteSpace(c.Taxpayer.Name)) throw new CaseInvalidException("Name des Steuerpflichtigen darf nicht leer sein");
        if (string.IsNullOrWhiteSpace(c.Taxpayer.TaxNumber)) throw new CaseInvalidException("Steuernummer darf nicht leer sein");
        if (string.IsNullOrWhiteSpace(c.Taxpayer.PabNumber)) throw new CaseInvalidException("PAB-Nr. darf nicht leer sein");
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
            if (p.Recipe is null) continue;
            if (p.Recipe.Count == 0) throw new CaseInvalidException($"Produkt \"{p.ProductId}\": Rezeptur darf nicht leer sein");
            foreach (var l in p.Recipe)
            {
                if (string.IsNullOrEmpty(l.IngredientId))
                    throw new CaseInvalidException($"Produkt \"{p.ProductId}\": Rezeptur enthält eine Zeile ohne Zutat");
                if (l.Amount <= 0)
                    throw new CaseInvalidException($"Produkt \"{p.ProductId}\": Menge der Zutat \"{l.IngredientId}\" muss größer als 0 sein");
                if (Units.Lookup(l.Unit) is null)
                    throw new CaseInvalidException($"Produkt \"{p.ProductId}\": Zutat \"{l.IngredientId}\" hat die unbekannte Einheit \"{l.Unit}\"");
            }
        }
        var invoices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var inv in c.Invoices)
        {
            if (string.IsNullOrEmpty(inv.Id)) throw new CaseInvalidException("Beleg ohne ID");
            if (!ValidId(inv.Id)) throw new CaseInvalidException($"ungültige Beleg-ID \"{inv.Id}\"");
            if (!invoices.Add(inv.Id)) throw new CaseInvalidException($"Beleg \"{inv.Id}\" mehrfach angegeben");
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

    static string Day(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateTimeOffset d) => d.ToString("O", CultureInfo.InvariantCulture);

    static string? Str(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetString(i);

    static long? Num(SqliteDataReader r, int i) => r.IsDBNull(i) ? null : r.GetInt64(i);

    static DateOnly? Date(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : DateOnly.ParseExact(r.GetString(i), "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateTimeOffset When(SqliteDataReader r, int i) =>
        DateTimeOffset.Parse(r.GetString(i), CultureInfo.InvariantCulture);

    static void ReadRows(SqliteConnection db, string sql, Action<SqliteDataReader> row,
        params (string Name, object? Value)[] args)
    {
        using var cmd = Command(db, null, sql, args);
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

    static object? Scalar(SqliteConnection db, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Command(db, null, sql, args);
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
            throw new StoreUnavailableException("Datenbank: " + e.Message, e);
        }
    }
}

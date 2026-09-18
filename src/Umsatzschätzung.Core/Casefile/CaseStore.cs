using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Casefile;

public sealed class CaseInvalidException(string message, Exception? inner = null) : Exception(message, inner);

public sealed class CaseNotFoundException(string message) : Exception(message);

public sealed partial class CaseStore(string dir)
{
    // Beside the document, skipped by LoadFile like every other dot file: what the scan was read
    // as, so a stored invoice can still show where each of its values came from.
    const string ReadingName = ".reading.json";

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex IdPattern();

    public static bool ValidId(string id) => IdPattern().IsMatch(id) && !id.Contains("..");

    string PathOf(string id) =>
        ValidId(id) ? Path.Combine(dir, id + ".json") : throw new CaseInvalidException($"ungültige Fall-ID \"{id}\"");

    string FileDir(string caseId, string invoiceId) =>
        ValidId(caseId) && ValidId(invoiceId)
            ? Path.Combine(dir, caseId + ".files", invoiceId)
            : throw new CaseInvalidException($"ungültige ID \"{caseId}\"/\"{invoiceId}\"");

    public List<Case> List()
    {
        if (!Directory.Exists(dir)) return [];
        var cases = new List<Case>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json"))
        {
            var name = Path.GetFileName(path);
            if (name.StartsWith('.')) continue;
            try
            {
                cases.Add(Decode(File.ReadAllBytes(path)));
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
    }

    public Case Load(string id)
    {
        var path = PathOf(id);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Fall \"{id}\": Fall nicht gefunden");
        Case c;
        try
        {
            c = Decode(File.ReadAllBytes(path));
        }
        catch (CaseInvalidException e)
        {
            throw new CaseInvalidException($"Fall \"{id}\": {e.Message}", e);
        }
        if (c.Id != id) throw new CaseInvalidException($"Fall \"{id}\": Datei enthält Fall \"{c.Id}\"");
        return c;
    }

    public void Save(Case c) => WriteAtomic(PathOf(c.Id), Encode(c));

    public void Delete(string id)
    {
        var path = PathOf(id);
        var files = path[..^".json".Length] + ".files";
        if (Directory.Exists(files)) Directory.Delete(files, true);
        if (!File.Exists(path)) throw new CaseNotFoundException($"Fall \"{id}\": Fall nicht gefunden");
        File.Delete(path);
    }

    public void SaveFile(string caseId, string invoiceId, string name, byte[] data)
    {
        var target = FileDir(caseId, invoiceId);
        name = Path.GetFileName(name);
        if (name == "" || name.StartsWith('.')) throw new CaseInvalidException($"ungültiger Dateiname \"{name}\"");
        Directory.CreateDirectory(target);
        // An invoice keeps one document, so the old one goes; what is stored beside it stays.
        foreach (var old in Directory.EnumerateFiles(target).Where(f => !Path.GetFileName(f).StartsWith('.')).ToList())
            File.Delete(old);
        WriteAtomic(Path.Combine(target, name), data);
    }

    public void DeleteFile(string caseId, string invoiceId)
    {
        var target = FileDir(caseId, invoiceId);
        if (Directory.Exists(target)) Directory.Delete(target, true);
    }

    public void SaveReading(string caseId, string invoiceId, byte[] data) =>
        WriteAtomic(Path.Combine(FileDir(caseId, invoiceId), ReadingName), data);

    public byte[]? LoadReading(string caseId, string invoiceId)
    {
        var path = Path.Combine(FileDir(caseId, invoiceId), ReadingName);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    public (string Name, byte[] Data) LoadFile(string caseId, string invoiceId)
    {
        var target = FileDir(caseId, invoiceId);
        if (!Directory.Exists(target)) throw new CaseNotFoundException($"Beleg {invoiceId}: Fall nicht gefunden");
        var path = Directory.EnumerateFiles(target)
            .Where(p => !Path.GetFileName(p).StartsWith('.'))
            .Order(StringComparer.Ordinal)
            .FirstOrDefault()
            ?? throw new CaseNotFoundException($"Beleg {invoiceId}: Fall nicht gefunden");
        return (Path.GetFileName(path), File.ReadAllBytes(path));
    }

    static void WriteAtomic(string path, byte[] data)
    {
        var parent = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(parent);
        var tmp = Path.Combine(parent, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                f.Write(data);
                f.Flush(true);
            }
            File.Move(tmp, path, true);
        }
        catch
        {
            File.Delete(tmp);
            throw;
        }
    }

    public static byte[] Encode(Case c)
    {
        Defaults(c);
        Validate(c);
        return Encoding.UTF8.GetBytes(Json.Serialize(c) + "\n");
    }

    public static Case Decode(ReadOnlySpan<byte> data)
    {
        Case c;
        try
        {
            c = Json.Deserialize<Case>(data);
        }
        catch (JsonException e)
        {
            throw new CaseInvalidException("Falldatei ungültig: " + e.Message, e);
        }
        Defaults(c);
        Validate(c);
        return c;
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
}

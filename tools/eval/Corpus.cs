using System.Text.Json;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Ocr;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Eval;

public sealed record Page(int Width, int Height, List<TaggedWord> Words);

public sealed record Variation(string Invoice, string Template, List<Page> Pages);

public static class Corpus
{
    // The dumps tools/ocr wrote beside the fixtures, untagged: the words go through the app's
    // own row grouping and tagger, so this scores the app. Only the scans are read. The clean
    // raster of a source PDF is a page no auditor ever hands in, and reading it perfectly says
    // nothing about the paper that arrives in the post.
    public const string ScanDump = ".scan" + Dump.Suffix;

    public static IEnumerable<Variation> ReadDumps(string corpus)
    {
        foreach (var file in Directory.EnumerateFiles(corpus, "*" + ScanDump, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            var pages = Dump.Read(file).Pages
                .Select(p => new Page(p.Width, p.Height, [.. p.OcrWords().Select(w => new TaggedWord(w, null, Tagging.Role.LineItem, -1))]))
                .ToList();
            var name = Path.GetFileName(file)[..^Dump.Suffix.Length];
            yield return new Variation(name, Path.GetFileName(Path.GetDirectoryName(file)!), pages);
        }
    }

    // page.jsonl as tools/train writes it: labelled or predicted words, one page per line.
    public static IEnumerable<Variation> ReadRows(string rows, string split)
    {
        var groups = new Dictionary<(string, string), List<(int No, Page Page)>>();
        foreach (var line in File.ReadLines(rows))
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (split != "" && root.GetProperty("split").GetString() != split) continue;
            var key = (root.GetProperty("invoice").GetString()!, root.GetProperty("template").GetString()!);
            var page = new Page(root.GetProperty("w").GetInt32(), root.GetProperty("h").GetInt32(),
                Words(root.GetProperty("words")));
            groups.TryAdd(key, []);
            groups[key].Add((root.GetProperty("page").GetInt32(), page));
        }
        foreach (var ((invoice, template), pages) in groups)
            yield return new Variation(invoice, template, [.. pages.OrderBy(p => p.No).Select(p => p.Page)]);
    }

    // pred wins over field, so one reader scores a prediction or verifies a ground truth.
    static List<TaggedWord> Words(JsonElement words)
    {
        var tagged = new List<(int Row, double X, TaggedWord Word)>();
        foreach (var w in words.EnumerateArray())
        {
            var label = w.TryGetProperty("pred", out var pred) ? pred.GetString()! : w.GetProperty("field").GetString()!;
            var box = w.GetProperty("box");
            var x = box[0].GetDouble();
            var word = new OcrWord
            {
                Text = w.GetProperty("t").GetString()!,
                Box = new Box(Round(x), Round(box[1].GetDouble()), Round(box[2].GetDouble()), Round(box[3].GetDouble())),
            };
            var row = w.GetProperty("row").GetInt32();
            tagged.Add((row, x, new TaggedWord(word, Field(label), Role(w.GetProperty("role").GetString()!), row,
                w.TryGetProperty("conf", out var cf) ? cf.GetSingle() : 1f,
                w.TryGetProperty("col", out var c) ? c.GetInt32() : 0,
                w.TryGetProperty("cell_start", out var cs) && cs.GetInt32() == 1)));
        }
        return [.. tagged.OrderBy(t => t.Row).ThenBy(t => t.X).Select(t => t.Word)];
    }

    static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    // The names in tools/train/schema.py.
    static Field? Field(string label) => label switch
    {
        "cell" => Model.Field.Cell,
        "vat" => Model.Field.Vat,
        "invoiceNumber" => Model.Field.InvoiceNumber,
        "invoiceDate" => Model.Field.InvoiceDate,
        "supplier" => Model.Field.Supplier,
        "netTotal" => Model.Field.NetTotal,
        "grossTotal" => Model.Field.GrossTotal,
        "numberLabel" => Model.Field.NumberLabel,
        "dateLabel" => Model.Field.DateLabel,
        "netLabel" => Model.Field.NetLabel,
        "grossLabel" => Model.Field.GrossLabel,
        "vatLabel" => Model.Field.VatLabel,
        "otherLabel" => Model.Field.OtherLabel,
        _ => null,
    };

    static Role Role(string role) => role switch
    {
        "header" => Tagging.Role.Header,
        "column-header" => Tagging.Role.ColumnHeader,
        "line-item" => Tagging.Role.LineItem,
        "line-wrap" => Tagging.Role.LineWrap,
        "continuation" => Tagging.Role.Continuation,
        "total" => Tagging.Role.Total,
        "footer" => Tagging.Role.Footer,
        "group" => Tagging.Role.Group,
        _ => Tagging.Role.Carry,
    };

    // <corpus>/<invoice>/expected.json for the generated corpus; for the real scans
    // <supplier>/<invoice>.pdf.expected.json, shared by every ".pdf.<variation>" of the invoice
    // and preferred over the one read off the e-invoice XML beside it.
    public static string ExpectedPath(string corpus, string invoice)
    {
        var generated = Path.Combine(corpus, invoice, "expected.json");
        if (File.Exists(generated)) return generated;
        var stem = invoice.IndexOf(".pdf.", StringComparison.Ordinal) is var cut and >= 0 ? invoice[..cut] : invoice;
        return Directory.EnumerateFiles(corpus, stem + ".*.expected.json", SearchOption.AllDirectories)
            .OrderBy(f => f.EndsWith(".pdf.expected.json") ? 0 : 1).ThenBy(f => f, StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new FileNotFoundException($"keine expected.json für {invoice} unter {corpus}");
    }

    public static Invoice Expected(string path) => Json.Deserialize<Invoice>(File.ReadAllText(path));
}

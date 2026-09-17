using System.Text.Json;
using System.Text.RegularExpressions;
using Umsatzschätzung.Model;
using Umsatzschätzung.Tagging;

namespace Umsatzschätzung.Eval;

public sealed record Variation(string Invoice, string Template, List<List<TaggedWord>> Pages, bool PerLineVat);

// page.jsonl and expected.json, as tools/corpus and tools/train --dump write them.
public static class Corpus
{
    public static IEnumerable<Variation> Read(string rows, string split, bool repair)
    {
        var groups = new Dictionary<(string, string), List<(int Page, List<TaggedWord> Words, bool Vat)>>();
        foreach (var line in File.ReadLines(rows))
        {
            using var page = JsonDocument.Parse(line);
            var root = page.RootElement;
            if (split != "" && root.GetProperty("split").GetString() != split) continue;
            var key = (root.GetProperty("invoice").GetString()!, root.GetProperty("template").GetString()!);
            var (words, vat) = Words(root.GetProperty("words"), repair);
            groups.TryAdd(key, []);
            groups[key].Add((root.GetProperty("page").GetInt32(), words, vat));
        }
        foreach (var ((invoice, template), pages) in groups)
            yield return new Variation(invoice, template,
                [.. pages.OrderBy(p => p.Page).Select(p => p.Words)],
                pages.Any(p => p.Vat));
    }

    static (List<TaggedWord> Words, bool PerLineVat) Words(JsonElement words, bool repair)
    {
        var tagged = new List<(int Row, double X, TaggedWord Word)>();
        var perLineVat = false;
        foreach (var w in words.EnumerateArray())
        {
            var label = w.TryGetProperty("pred", out var pred) ? pred.GetString()! : w.GetProperty("field").GetString()!;
            var field = Field(label);
            var role = Role(w.GetProperty("role").GetString()!);
            if (field == Model.Field.Vat && role is Tagging.Role.LineItem or Tagging.Role.LineWrap or Tagging.Role.Continuation)
                perLineVat = true;
            var box = w.GetProperty("box");
            var x = box[0].GetDouble();
            var text = w.GetProperty("t").GetString()!;
            var word = new OcrWord
            {
                Text = repair ? Repair(label, text) : text,
                Box = new Box(Round(x), Round(box[1].GetDouble()), Round(box[2].GetDouble()), Round(box[3].GetDouble())),
            };
            var conf = w.TryGetProperty("conf", out var c) ? c.GetSingle() : 1f;
            var row = w.GetProperty("row").GetInt32();
            tagged.Add((row, x, new TaggedWord(word, field, role, row, conf)));
        }
        return ([.. tagged.OrderBy(t => t.Row).ThenBy(t => t.X).Select(t => t.Word)], perLineVat);
    }

    static int Round(double v) => (int)Math.Round(v, MidpointRounding.AwayFromZero);

    static Field? Field(string label) => label switch
    {
        "quantity" => Model.Field.Quantity,
        "unit" => Model.Field.Unit,
        "name" => Model.Field.Name,
        "articleId" => Model.Field.ArticleId,
        "unitPrice" => Model.Field.UnitPrice,
        "lineNet" => Model.Field.LineNet,
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

    // A field the tagger has already called numeric can only hold digits, so a letter in it is
    // an OCR slip. Applied per word by that word's own label, before assembly joins the cell.
    static readonly Dictionary<string, string> Digits = new()
    {
        ["ø"] = "0", ["Ø"] = "0", ["O"] = "0", ["o"] = "0", ["D"] = "0",
        ["l"] = "1", ["I"] = "1", ["i"] = "1", ["|"] = "1", ["!"] = "1",
        ["S"] = "5", ["s"] = "5", ["B"] = "8", ["Z"] = "2", ["z"] = "2",
        ["G"] = "6", ["b"] = "6", ["g"] = "9", ["q"] = "9", ["A"] = "4",
    };

    static readonly HashSet<string> Numeric =
        ["quantity", "unitPrice", "lineNet", "netTotal", "grossTotal", "vat"];

    static readonly Regex ReadsAsNumber = new(@"^[-+]?[€$]?\s*\d[\d.,\s]*\s*[%€]?[-]?$");

    static string Repair(string label, string text)
    {
        if (!Numeric.Contains(label) || text.Length == 0 || Number(text)) return text;
        var fixedText = string.Concat(text.Select(c => Digits.GetValueOrDefault(c.ToString(), c.ToString())));
        return Number(fixedText) ? fixedText : text;
    }

    static bool Number(string t) => ReadsAsNumber.IsMatch(t.Trim());

    public static Doc Expected(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var root = json.RootElement;
        if (!root.TryGetProperty("vatBreakdown", out _))
            throw new InvalidOperationException($"{path} ist keine echte expected.json (kein vatBreakdown).");
        return new Doc(
            Text(root, "number"), Text(root, "date"), Text(root, "supplierName"),
            root.GetProperty("netTotal").GetInt64(), root.GetProperty("grossTotal").GetInt64(),
            [.. root.GetProperty("lines").EnumerateArray().Select(l => new Line(
                Text(l, "name"), l.GetProperty("quantity").GetInt64(), Text(l, "unitCode"),
                l.GetProperty("unitPrice").GetInt64(), l.GetProperty("lineNet").GetInt64(),
                l.GetProperty("vat").GetInt64()))]);
    }

    static string Text(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
}

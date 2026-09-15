using Umsatzschätzung.Model;
using Umsatzschätzung.Tagging;

namespace Umsatzschätzung.Extract;

// Labelled words to an Invoice. Port of tools/eval/assemble.py, which is the reference the
// eval scores both models against; keep the two in step.
public static class Assemble
{
    static readonly Field[] Header =
        [Field.InvoiceNumber, Field.InvoiceDate, Field.Supplier, Field.NetTotal, Field.GrossTotal];

    // units: how a printed unit word becomes a code. The app resolves to its own codes in
    // Model/Units.cs; the eval resolves to the corpus vocabulary its expected.json carries.
    public static Invoice Invoice(IReadOnlyList<List<TaggedWord>> tagged, List<OcrPage> pages,
        Func<string, string>? units = null)
    {
        units ??= Parse.UnitCode;
        var inv = new Model.Invoice { Source = Source.Scan, Currency = "EUR" };
        for (var p = 0; p < tagged.Count; p++)
        {
            foreach (var f in Header)
                if (Block(tagged[p], f) is { } cell)
                    pages[p].Header[f] = cell;
            foreach (var item in Items(tagged[p]))
            {
                var cells = Cells(item);
                if (!cells.ContainsKey(Field.Name) && !cells.ContainsKey(Field.LineNet)) continue;
                var line = new InvoiceLine
                {
                    No = inv.Lines.Count + 1,
                    Name = Text(cells, Field.Name),
                    SellerArticleId = cells.TryGetValue(Field.ArticleId, out var article) ? article.Text : null,
                    Quantity = Parse.Number(Text(cells, Field.Quantity), Parse.ScaleMilli),
                    UnitCode = units(Text(cells, Field.Unit)),
                    UnitPrice = Parse.Number(Text(cells, Field.UnitPrice), Parse.ScaleMicro),
                    PriceBaseQty = 1000,
                    LineNet = Parse.Number(Text(cells, Field.LineNet), Parse.ScaleCents),
                    Vat = Parse.Number(Text(cells, Field.Vat), Parse.ScaleBp),
                };
                inv.Lines.Add(line);
                pages[p].Lines.Add(new OcrLine { Cells = cells, Parsed = line });
            }
        }

        if (TotalsVat(tagged) is { } rate)
            foreach (var l in inv.Lines.Where(l => l.Vat == 0)) l.Vat = rate;

        inv.SupplierName = First(pages, Field.Supplier) ?? "";
        inv.Number = First(pages, Field.InvoiceNumber) ?? "";
        inv.Date = Parse.Date(First(pages, Field.InvoiceDate) ?? "");
        inv.NetTotal = Parse.Number(First(pages, Field.NetTotal) ?? "", Parse.ScaleCents);
        inv.GrossTotal = Parse.Number(First(pages, Field.GrossTotal) ?? "", Parse.ScaleCents);
        return inv;
    }

    // A line item is a region: `line-item` opens one and the continuation roles extend it
    // over as many rows as the layout gave it — a wrapped name, a detail row, an amount
    // pushed onto its own line.
    static List<List<TaggedWord>> Items(List<TaggedWord> words)
    {
        var items = new List<List<TaggedWord>>();
        List<TaggedWord>? current = null;
        foreach (var row in words.GroupBy(w => w.Row))
        {
            var role = row.First().Role;
            if (current is not null && role is Role.LineWrap or Role.Continuation)
            {
                current.AddRange(row);
                continue;
            }
            if (current is not null) items.Add(current);
            current = role == Role.LineItem ? [.. row] : null;
        }
        if (current is not null) items.Add(current);
        return items;
    }

    // Most layouts print the rate once in the totals block instead of per line. Two rates
    // there cannot be attributed to lines without a per-line marker, so that yields nothing.
    static long? TotalsVat(IReadOnlyList<List<TaggedWord>> tagged)
    {
        var rates = new HashSet<long>();
        foreach (var page in tagged)
            foreach (var row in page.GroupBy(w => w.Row).Where(r => r.First().Role == Role.Total))
                foreach (var w in row.Where(w => w.Field == Field.Vat))
                {
                    var rate = Parse.Number(w.Word.Text, Parse.ScaleBp);
                    if (rate != 0) rates.Add(rate);
                }
        return rates.Count == 1 ? rates.Single() : null;
    }

    static Dictionary<Field, OcrWord> Cells(List<TaggedWord> item)
    {
        var cells = new Dictionary<Field, OcrWord>();
        foreach (var group in item.Where(w => w.Field is not null).GroupBy(w => w.Field!.Value))
            cells[group.Key] = Cell(group)!;
        return cells;
    }

    // A header field is the first contiguous run of rows carrying it. Not the first word,
    // which truncates "8. November 2025" to "8."; not every occurrence, which repeats the
    // supplier name once per letterhead, footer and later page.
    static OcrWord? Block(List<TaggedWord> words, Field f)
    {
        var block = new List<TaggedWord>();
        foreach (var row in words.GroupBy(w => w.Row))
        {
            var hit = row.Where(w => w.Field == f).ToList();
            if (hit.Count > 0) block.AddRange(hit);
            else if (block.Count > 0) break;
        }
        return Cell(block);
    }

    static OcrWord? Cell(IEnumerable<TaggedWord> words)
    {
        var list = words.ToList();
        if (list.Count == 0) return null;
        var box = new Box(0, 0, 0, 0);
        foreach (var w in list) box = Rows.Union(box, w.Word.Box);
        return new OcrWord { Text = string.Join(" ", list.Select(w => w.Word.Text)), Box = box };
    }

    static string Text(Dictionary<Field, OcrWord> cells, Field f) => cells.TryGetValue(f, out var w) ? w.Text : "";

    static string? First(List<OcrPage> pages, Field f)
    {
        foreach (var p in pages)
            if (p.Header.TryGetValue(f, out var w)) return w.Text;
        return null;
    }
}

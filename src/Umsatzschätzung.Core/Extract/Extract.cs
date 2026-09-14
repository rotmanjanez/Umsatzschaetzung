using Umsatzschätzung.Service;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Extract;

public sealed record ExtractPage(int Width, int Height, List<OcrWord> Words, byte[] Image);

public static class Extractor
{
    public static async Task<(Invoice Invoice, List<OcrPage> Pages)> InvoiceAsync(ILlmEngine engine, IReadOnlyList<ExtractPage> pages, CancellationToken ct)
    {
        var inv = new Invoice { Source = Source.Scan, Currency = "EUR" };
        var output = new List<OcrPage>(pages.Count);
        foreach (var p in pages)
        {
            var page = new OcrPage { Image = p.Image, Width = p.Width, Height = p.Height, Words = p.Words };
            var rows = Rows.GroupRows(p.Words);
            if (rows.Count > 0)
                Fill(page, rows, await Prompt.Ask(engine, rows, ct));
            Merge(inv, page);
            output.Add(page);
        }
        Distribute(output, Check.Invoice(inv));
        return (inv, output);
    }

    static void Fill(OcrPage page, List<Row> rows, Answer a)
    {
        foreach (var (f, text) in new (Field, string)[]
                 {
                     (Field.InvoiceNumber, a.Number),
                     (Field.InvoiceDate, a.Date),
                     (Field.Supplier, a.Supplier),
                     (Field.NetTotal, a.NetTotal),
                     (Field.GrossTotal, a.GrossTotal),
                 })
        {
            if (text.Trim() != "") page.Header[f] = LocateAnywhere(rows, text);
        }
        foreach (var l in a.Lines)
        {
            var cells = new Dictionary<Field, OcrWord>();
            foreach (var (f, text) in new (Field, string)[]
                     {
                         (Field.Name, l.Name),
                         (Field.Quantity, l.Quantity),
                         (Field.Unit, l.Unit),
                         (Field.UnitPrice, l.UnitPrice),
                         (Field.LineNet, l.LineNet),
                         (Field.Vat, l.Vat),
                     })
            {
                if (text.Trim() != "") cells[f] = Locate(rows, l.Row, text);
            }
            page.Lines.Add(new OcrLine
            {
                Cells = cells,
                Parsed = new InvoiceLine
                {
                    Name = l.Name.Trim(),
                    Quantity = Parse.Number(l.Quantity, Parse.ScaleMilli),
                    UnitCode = Parse.UnitCode(l.Unit),
                    UnitPrice = Parse.Number(l.UnitPrice, Parse.ScaleMicro),
                    PriceBaseQty = 1000,
                    LineNet = Parse.Number(l.LineNet, Parse.ScaleCents),
                    Vat = Parse.Number(l.Vat, Parse.ScaleBp),
                },
            });
        }
    }

    static OcrWord Locate(List<Row> rows, int idx, string text)
    {
        var cell = new OcrWord { Text = text.Trim() };
        if (idx < 0 || idx >= rows.Count) return cell;
        var target = Normalise(text);
        foreach (var d in (int[])[0, 1, -1, 2, -2])
        {
            var i = idx + d;
            if (i < 0 || i >= rows.Count) continue;
            var m = MatchWords(rows[i], target);
            if (m.Count > 0)
            {
                cell.Box = m.Box;
                return cell;
            }
        }
        cell.Box = rows[idx].Box;
        return cell;
    }

    static OcrWord LocateAnywhere(List<Row> rows, string text)
    {
        var cell = new OcrWord { Text = text.Trim() };
        var target = Normalise(text);
        var best = new Match(new Box(0, 0, 0, 0), 0);
        foreach (var r in rows)
        {
            var m = MatchWords(r, target);
            if (m.Count > best.Count) best = m;
        }
        if (best.Count > 0) cell.Box = best.Box;
        return cell;
    }

    readonly record struct Match(Box Box, int Count);

    static Match MatchWords(Row r, string target)
    {
        var m = new Match(new Box(0, 0, 0, 0), 0);
        foreach (var w in r.Words)
        {
            var n = Normalise(w.Text);
            if (n == "" || (n != target && (n.Length < 2 || !target.Contains(n)))) continue;
            m = new Match(Rows.Union(m.Box, w.Box), m.Count + n.Length);
        }
        return m;
    }

    static string Normalise(string s)
    {
        var b = new System.Text.StringBuilder();
        foreach (var r in s.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(r) || r == ',') b.Append(r);
        }
        return b.ToString();
    }

    static void Merge(Invoice inv, OcrPage page)
    {
        foreach (var l in page.Lines)
        {
            l.Parsed.No = inv.Lines.Count + 1;
            inv.Lines.Add(l.Parsed);
        }
        if (page.Header.TryGetValue(Field.InvoiceNumber, out var number) && inv.Number == "")
            inv.Number = number.Text;
        if (page.Header.TryGetValue(Field.InvoiceDate, out var date) && inv.Date is null)
            inv.Date = Parse.Date(date.Text);
        if (page.Header.TryGetValue(Field.Supplier, out var supplier) && inv.SupplierName == "")
            inv.SupplierName = supplier.Text;
        if (page.Header.TryGetValue(Field.NetTotal, out var net) && inv.NetTotal == 0)
            inv.NetTotal = Parse.Number(net.Text, Parse.ScaleCents);
        if (page.Header.TryGetValue(Field.GrossTotal, out var gross) && inv.GrossTotal == 0)
            inv.GrossTotal = Parse.Number(gross.Text, Parse.ScaleCents);
    }

    static void Distribute(List<OcrPage> pages, List<Flag> flags)
    {
        if (pages.Count == 0) return;
        foreach (var f in flags)
        {
            if (f.LineNo == 0)
            {
                pages[0].Flags.Add(f);
                continue;
            }
            var placed = false;
            foreach (var l in pages.SelectMany(p => p.Lines).Where(l => l.Parsed.No == f.LineNo))
            {
                l.Flags.Add(f);
                placed = true;
            }
            if (!placed) pages[0].Flags.Add(f);
        }
    }
}

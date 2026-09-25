using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Extract;

// Regions of one page read again, each as the words found in it, in the frame of the reading.
public delegate Task<List<List<OcrWord>>> Reread(int page, IReadOnlyList<Box> regions, CancellationToken ct);

// An amount or unit the detector drew no box around, where three of the four cells around it
// say where it was printed: the column above and below gives its width, the row beside it its
// height. A read amount is kept only if the row then adds up, a read unit only if it is the
// unit of the column around it.
public static class Gaps
{
    static readonly Field[] Cells = [Field.Quantity, Field.Unit, Field.UnitPrice, Field.LineNet];

    public static async Task Fill(List<OcrPage> pages, Reread reread, CancellationToken ct)
    {
        for (var p = 0; p < pages.Count; p++)
        {
            var lines = pages[p].Lines;
            var gaps = new List<(int Line, Field Field, Box Box)>();
            for (var i = 0; i < lines.Count; i++)
                foreach (var f in Cells)
                    if (!lines[i].Cells.ContainsKey(f) && Estimate(lines, i, f) is { } box)
                        gaps.Add((i, f, box));
            if (gaps.Count == 0) continue;
            var read = await reread(p, [.. gaps.Select(g => g.Box)], ct);
            foreach (var line in gaps.Zip(read).Where(x => x.Second.Count > 0).GroupBy(x => x.First.Line))
            {
                foreach (var unit in line.Where(x => x.First.Field == Field.Unit))
                    AcceptUnit(lines, line.Key, Join(unit.Second));
                Accept(lines[line.Key], [.. line.Where(x => x.First.Field != Field.Unit).Select(x => (x.First.Field, Join(x.Second)))]);
            }
        }
    }

    public static Box? Estimate(IReadOnlyList<OcrLine> lines, int index, Field field)
    {
        var above = index > 0 ? lines[index - 1].Cells.GetValueOrDefault(field)?.Box : null;
        var below = index + 1 < lines.Count ? lines[index + 1].Cells.GetValueOrDefault(field)?.Box : null;
        if (Union(above, below) is not { } column) return null;
        var row = lines[index].Cells.Values.Select(c => c.Box).ToList();
        if (row.Any(b => b.X < column.X + column.W && column.X < b.X + b.W)) return null;
        var left = row.Where(b => b.X + b.W <= column.X).MaxBy(b => b.X + b.W);
        var right = row.Where(b => b.X >= column.X + column.W).MinBy(b => b.X);
        if (new[] { above, below, left, right }.Count(b => b is not null) < 3) return null;

        var beside = Union(left, right)!;
        var height = beside.H;
        var middle = beside.Y + height / 2;
        if (above is not null && above.Y + above.H > middle || below is not null && below.Y < middle) return null;

        // Amounts are right-aligned: a wider one than its neighbours reaches further left.
        var x0 = Math.Max(left is null ? 0 : left.X + left.W, column.X - column.W);
        var x1 = Math.Min(right?.X ?? int.MaxValue, column.X + column.W + height);
        var pad = height / 4;
        return new Box(x0, beside.Y - pad, x1 - x0, height + 2 * pad);
    }

    // Every read amount of the row is taken, or none of them.
    public static bool Accept(OcrLine line, IReadOnlyList<(Field Field, OcrWord Word)> reads)
    {
        var l = line.Parsed;
        var (quantity, price, net) = (l.Quantity, l.UnitPrice, l.LineNet);
        var kept = new List<(Field, OcrWord)>();
        foreach (var (field, word) in reads)
        {
            var text = Parse.Digits(word.Text);
            if (field == Field.Quantity && Parse.Number(text, Parse.ScaleMilli) is var q and not 0) quantity = q;
            else if (field == Field.UnitPrice && Parse.Number(text, Parse.ScaleMicro) is var p and not 0) price = p;
            else if (field == Field.LineNet && Parse.Number(text, Parse.ScaleCents) is var n and not 0) net = n;
            else continue;
            kept.Add((field, word));
        }
        if (kept.Count == 0 || quantity == 0 || price == 0 || net == 0) return false;
        if (InvoiceMath.LineNet(quantity, price, l.PriceBaseQty) != net) return false;
        (l.Quantity, l.UnitPrice, l.LineNet) = (quantity, price, net);
        foreach (var (field, word) in kept) line.Cells[field] = word;
        return true;
    }

    public static bool AcceptUnit(IReadOnlyList<OcrLine> lines, int index, OcrWord word)
    {
        var code = Parse.UnitCode(word.Text);
        if (Units.Lookup(code) is null) return false;
        var column = new[] { index - 1, index + 1 }
            .Where(i => i >= 0 && i < lines.Count && lines[i].Cells.ContainsKey(Field.Unit))
            .Select(i => lines[i].Parsed.UnitCode).ToList();
        if (column.Count == 0 || column.Any(c => c != code)) return false;
        lines[index].Parsed.UnitCode = code;
        lines[index].Cells[Field.Unit] = word;
        return true;
    }

    static OcrWord Join(List<OcrWord> words) => new()
    {
        Text = string.Join(" ", words.OrderBy(w => w.Box.X).Select(w => w.Text)),
        Box = words.Select(w => w.Box).Aggregate(Rows.Union),
        Confidence = words.Min(w => w.Confidence),
    };

    static Box? Union(Box? a, Box? b) => a is null ? b : b is null ? a : Rows.Union(a, b);
}

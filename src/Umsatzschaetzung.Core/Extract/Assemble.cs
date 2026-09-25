using System.Globalization;
using System.Text.RegularExpressions;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Extract;

public static class Assemble
{
    static readonly Field[] Header =
        [Field.InvoiceNumber, Field.InvoiceDate, Field.Supplier, Field.NetTotal, Field.GrossTotal];

    static readonly Dictionary<Field, Field> LabelOf = new()
    {
        [Field.InvoiceNumber] = Field.NumberLabel,
        [Field.InvoiceDate] = Field.DateLabel,
        [Field.NetTotal] = Field.NetLabel,
        [Field.GrossTotal] = Field.GrossLabel,
        [Field.Vat] = Field.VatLabel,
    };

    static readonly Dictionary<Field, Role> ValueRole = new()
    {
        [Field.InvoiceNumber] = Role.Header,
        [Field.InvoiceDate] = Role.Header,
        [Field.Supplier] = Role.Header,
        [Field.NetTotal] = Role.Total,
        [Field.GrossTotal] = Role.Total,
    };

    static bool IsLabel(Field? f) => f is Field.NumberLabel or Field.DateLabel or Field.NetLabel
        or Field.GrossLabel or Field.VatLabel or Field.OtherLabel;

    const double NearX = 0.05;
    const double Radius = 0.25;
    const double Gap = 1.5;

    public static Invoice Invoice(IReadOnlyList<List<TaggedWord>> tagged, List<OcrPage> pages)
    {
        var inv = new Model.Invoice { Source = Source.Scan, Currency = "EUR" };
        Table? layout = null;
        for (var p = 0; p < tagged.Count; p++)
        {
            var table = Table.Read(tagged[p], layout);
            if (table.HasColumns) layout = table;
            foreach (var cells in table.Items)
            {
                // A position is billed: a row without any number is a note the row role mistook.
                if (!cells.ContainsKey(Field.Name) && !cells.ContainsKey(Field.LineNet)) continue;
                if (!cells.ContainsKey(Field.LineNet) && !cells.ContainsKey(Field.Quantity) && !cells.ContainsKey(Field.UnitPrice)) continue;
                var unit = Parse.UnitCode(Text(cells, Field.Unit));
                var quantity = Parse.Digits(Text(cells, Field.Quantity));
                var line = new InvoiceLine
                {
                    No = inv.Lines.Count + 1,
                    Name = PackSize.Recover(Parse.PackUnit(Text(cells, Field.Name)), unit),
                    SellerArticleId = cells.TryGetValue(Field.ArticleId, out var article) ? article.Text : null,
                    Quantity = Parse.Number(quantity, Parse.ScaleMilli),
                    UnitCode = unit,
                    UnitPrice = Parse.Number(Parse.Digits(Text(cells, Field.UnitPrice)), Parse.ScaleMicro),
                    PriceBaseQty = 1000,
                    LineNet = Parse.Number(Parse.Digits(Text(cells, Field.LineNet)), Parse.ScaleCents),
                    Vat = Parse.Number(Parse.Digits(Text(cells, Field.Vat)), Parse.ScaleBp),
                };
                Regroup(line, quantity);
                Repair(line, quantity, Parse.Digits(Text(cells, Field.UnitPrice)), Parse.Digits(Text(cells, Field.LineNet)));
                inv.Lines.Add(line);
                pages[p].Lines.Add(new OcrLine { Cells = cells, Parsed = line });
            }
        }

        if (TotalsVat(tagged) is { } stated)
        {
            foreach (var l in inv.Lines.Where(l => l.Vat == 0)) l.Vat = stated.Rate;
            pages[stated.Page].Header[Field.Vat] = stated.Word;
        }

        var header = new Dictionary<Field, string>();
        foreach (var f in Header)
            if (HeaderValue(tagged, f) is { } hit)
            {
                pages[hit.Page].Header[f] = new OcrWord { Text = hit.Run.Text, Box = Union(hit.Run.Words) };
                header[f] = hit.Run.Text;
            }

        inv.SupplierName = header.GetValueOrDefault(Field.Supplier, "");
        inv.Number = header.GetValueOrDefault(Field.InvoiceNumber, "");
        inv.Date = Parse.Date(header.GetValueOrDefault(Field.InvoiceDate, ""));
        inv.StatedNet = Stated(header, Field.NetTotal);
        inv.StatedGross = Stated(header, Field.GrossTotal);
        return inv;
    }

    // "5,450 kg" is five and a half kilos, but German grouping writes thousands the same way
    // and Parse.Number reads 5450; only that shape is ambiguous, and the row decides.
    static readonly Regex Grouped = new(@"^\d{1,3}[.,\s]\d{3}$");

    public static void Regroup(InvoiceLine line, string quantityText)
    {
        if (line.LineNet == 0 || line.UnitPrice == 0 || line.PriceBaseQty == 0) return;
        if (line.Quantity % 1000 != 0 || !Grouped.IsMatch(quantityText.Trim())) return;
        if (Adds(line.Quantity, line.UnitPrice, line.PriceBaseQty, line.LineNet)) return;
        if (Adds(line.Quantity / 1000, line.UnitPrice, line.PriceBaseQty, line.LineNet)) line.Quantity /= 1000;
    }

    // Digits a worn scan swaps because they share a shape, in both directions.
    static readonly Dictionary<char, string> Confusable = Shapes(
        ["08", "06", "09", "17", "38", "56", "58", "68", "69", "27", "49", "39"]);

    static Dictionary<char, string> Shapes(string[] pairs)
    {
        var map = new Dictionary<char, string>();
        foreach (var pair in pairs)
        {
            map[pair[0]] = map.GetValueOrDefault(pair[0], "") + pair[1];
            map[pair[1]] = map.GetValueOrDefault(pair[1], "") + pair[0];
        }
        return map;
    }

    // One misread digit, one stray digit before or after a cell, or one stray minus, put back
    // by the row: quantity times unit price is the line net. Only where exactly one edit of all
    // of them satisfies it; else the check reports it.
    public static void Repair(InvoiceLine line, string quantity = "", string price = "", string net = "")
    {
        if (Restore(line)) return;
        if (line.Quantity == 0 || line.UnitPrice == 0 || line.LineNet == 0) return;
        if (Adds(line.Quantity, line.UnitPrice, line.PriceBaseQty, line.LineNet)) return;

        // Two minus signs are a return; either could be the stray one, so neither is.
        var lone = (line.Quantity < 0 ? 1 : 0) + (line.UnitPrice < 0 ? 1 : 0) + (line.LineNet < 0 ? 1 : 0) == 1;
        var found = new List<(int Field, long Value)>();
        foreach (var q in Candidates(line.Quantity, quantity, Parse.ScaleMilli, lone))
            if (Adds(q, line.UnitPrice, line.PriceBaseQty, line.LineNet)) found.Add((1, q));
        foreach (var p in Candidates(line.UnitPrice, price, Parse.ScaleMicro, lone))
            if (Adds(line.Quantity, p, line.PriceBaseQty, line.LineNet)) found.Add((2, p));
        foreach (var n in Candidates(line.LineNet, net, Parse.ScaleCents, lone))
            if (Adds(line.Quantity, line.UnitPrice, line.PriceBaseQty, n)) found.Add((3, n));
        if (found.Count != 1) return;

        var (field, value) = found[0];
        if (field == 1) line.Quantity = value;
        else if (field == 2) line.UnitPrice = value;
        else line.LineNet = value;
    }

    static HashSet<long> Candidates(long value, string text, int scale, bool lone)
    {
        var seen = Confusions(value);
        foreach (var edit in Strays(text.Trim(), lone && value < 0))
            if (Parse.Number(edit, scale) is var v and not 0 && v != value) seen.Add(v);
        return seen;
    }

    // A table rule before the amount reads as a 1 or a minus, a mark after it as a digit.
    static IEnumerable<string> Strays(string text, bool minus)
    {
        if (minus && text.StartsWith('-')) yield return text[1..];
        var first = text.IndexOfAny(Digit);
        if (first < 0) yield break;
        var rest = text[(first + 1)..];
        if (rest.Length > 1 && rest[0] is '.' or ' ' && char.IsAsciiDigit(rest[1])) rest = rest[1..];
        if (rest.Length > 0 && char.IsAsciiDigit(rest[0]) && !(rest[0] == '0' && rest.Length > 1 && char.IsAsciiDigit(rest[1])))
            yield return text[..first] + rest;
        var last = text.LastIndexOfAny(Digit);
        if (last > first) yield return text[..last] + text[(last + 1)..];
    }

    static readonly char[] Digit = [.. "0123456789"];

    // A cell the scan lost outright comes back from the other two where the division is
    // exact. Never the line net: quantity times price is its definition, not a check.
    static bool Restore(InvoiceLine line)
    {
        if (line.LineNet == 0) return false;
        var baseQty = line.PriceBaseQty > 0 ? line.PriceBaseQty : 1000;
        if (line.Quantity == 0 && line.UnitPrice != 0)
        {
            if (QuantityFrom(line.LineNet, baseQty, line.UnitPrice) is not { } q) return false;
            line.Quantity = q;
            return true;
        }
        if (line.UnitPrice == 0 && line.Quantity != 0)
        {
            if (PriceFrom(line.LineNet, baseQty, line.Quantity) is { } p)
            {
                line.UnitPrice = p;
                return true;
            }
            // The quantity may itself be misread; a price only comes of the one confusion of
            // it that divides out. Asked of a lost quantity the same question answers too
            // often to be evidence: a price has to land on a whole cent, one chance in a
            // hundred per candidate, a quantity only on a whole hundredth of its unit.
            var found = new List<(long Quantity, long Price)>();
            foreach (var q in Confusions(line.Quantity))
                if (PriceFrom(line.LineNet, baseQty, q) is { } price) found.Add((q, price));
            if (found.Count != 1) return false;
            (line.Quantity, line.UnitPrice) = found[0];
            return true;
        }
        return false;
    }

    // Quantities are printed to the thousandth and prices to the cent, so a division that
    // lands anywhere else did not undo a multiplication.
    static long? QuantityFrom(long net, long baseQty, long price) =>
        Whole(InvoiceMath.RoundDiv(net * baseQty * 10000, price), 10) is { } q
            && Adds(q, price, baseQty, net) ? q : null;

    static long? PriceFrom(long net, long baseQty, long quantity) =>
        Whole(InvoiceMath.RoundDiv(net * baseQty * 10000, quantity), 10000) is { } p
            && Adds(quantity, p, baseQty, net) ? p : null;

    static long? Whole(long value, long step) => value != 0 && value % step == 0 ? value : null;

    static HashSet<long> Confusions(long value)
    {
        var sign = Math.Sign(value);
        var digits = Math.Abs(value).ToString(CultureInfo.InvariantCulture);
        var seen = new HashSet<long>();
        for (var i = 0; i < digits.Length; i++)
            foreach (var swap in Confusable.GetValueOrDefault(digits[i], ""))
            {
                if (i == 0 && swap == '0' && digits.Length > 1) continue;
                seen.Add(sign * long.Parse(digits[..i] + swap + digits[(i + 1)..], CultureInfo.InvariantCulture));
            }
        seen.Remove(value);
        return seen;
    }

    static bool Adds(long quantity, long price, long baseQty, long net) =>
        InvoiceMath.LineNet(quantity, price, baseQty) == net;

    static long? Stated(Dictionary<Field, string> header, Field field) =>
        Parse.Number(Parse.Digits(header.GetValueOrDefault(field, "")), Parse.ScaleCents) is var v and > 0 ? v : null;

    // Two stated rates cannot be attributed to lines; the vatLabel retry drops a rate read
    // off some other totals row.
    static (long Rate, int Page, OcrWord Word)? TotalsVat(IReadOnlyList<List<TaggedWord>> tagged)
    {
        var rates = Rates(tagged, labelledOnly: false);
        if (rates.Count > 1) rates = Rates(tagged, labelledOnly: true);
        if (rates.Count != 1) return null;
        var (rate, at) = rates.Single();
        return (rate, at.Page, at.Word);
    }

    // Where a rate is stated more than once, its first mention is where it came from.
    static Dictionary<long, (int Page, OcrWord Word)> Rates(IReadOnlyList<List<TaggedWord>> tagged, bool labelledOnly)
    {
        var rates = new Dictionary<long, (int Page, OcrWord Word)>();
        for (var p = 0; p < tagged.Count; p++)
            foreach (var row in Group(tagged[p]).Where(r => r[0].Role == Role.Total))
            {
                if (labelledOnly && !row.Any(w => w.Field == Field.VatLabel)) continue;
                foreach (var w in row.Where(w => w.Field == Field.Vat))
                {
                    var rate = Parse.Number(Parse.Digits(w.Word.Text), Parse.ScaleBp);
                    if (rate == 0 || rates.ContainsKey(rate)) continue;
                    var said = row.Where(v => v.Field == Field.VatLabel).Append(w).ToList();
                    rates[rate] = (p, new OcrWord { Text = w.Word.Text, Box = Union(said), Confidence = w.Word.Confidence });
                }
            }
        return rates;
    }

    static string Text(Dictionary<Field, OcrWord> cells, Field f) => cells.TryGetValue(f, out var w) ? w.Text : "";

    static List<List<TaggedWord>> Group(List<TaggedWord> words) =>
        [.. words.GroupBy(w => w.Row).OrderBy(g => g.Key).Select(g => g.ToList())];

    static Box Union(IEnumerable<TaggedWord> words)
    {
        var box = new Box(0, 0, 0, 0);
        foreach (var w in words) box = Rows.Union(box, w.Word.Box);
        return box;
    }

    sealed class Run
    {
        public int Row;
        public Role Role;
        public List<List<TaggedWord>> Lines = [];
        public Box Last = new(0, 0, 0, 0);
        public List<TaggedWord> Words = [];
        public Box Head = new(0, 0, 0, 0);
        public string Text = "";
        public double Conf;
    }

    // Labels claim first, on whichever page: a number read off the edge of the letterhead is
    // a guess, the one beside "Rechnungsnummer:" on page 2 is not. Without a label, role and
    // confidence decide and the first page with any run does. tools/eval/README.md has the
    // full order.
    static (int Page, Run Run)? HeaderValue(IReadOnlyList<List<TaggedWord>> tagged, Field field)
    {
        if (LabelOf.TryGetValue(field, out var label))
            for (var p = 0; p < tagged.Count; p++)
            {
                var rows = Group(tagged[p]);
                var runs = Runs(rows, field);
                if (runs.Count > 0 && Keyed(rows, runs, label, Extent(tagged[p])) is { } keyed) return (p, keyed);
            }

        var role = ValueRole.TryGetValue(field, out var r) ? (Role?)r : null;
        var longest = field == Field.Supplier;
        for (var p = 0; p < tagged.Count; p++)
        {
            var runs = Runs(Group(tagged[p]), field);
            if (runs.Count == 0) continue;
            Run? pick = null;
            foreach (var run in runs)
                if (pick is null || Better(run, pick, role, longest)) pick = run;
            return (p, pick!);
        }
        return null;
    }

    static bool Better(Run a, Run b, Role? role, bool longest)
    {
        var ra = role is not null && a.Role == role;
        var rb = role is not null && b.Role == role;
        if (ra != rb) return ra;
        if (a.Conf != b.Conf) return a.Conf > b.Conf;
        if (longest && a.Words.Count != b.Words.Count) return a.Words.Count > b.Words.Count;
        return a.Row < b.Row;
    }

    static double Extent(List<TaggedWord> words) =>
        words.Count == 0 ? 1.0 : Math.Max(1.0, words.Max(w => w.Word.Box.X + w.Word.Box.W));

    // A segment with a key directly before it starts a new block: "Kundennummer 48211" above
    // "Rechnungsnummer RE250292/6" are two values.
    static List<Run> Runs(List<List<TaggedWord>> rows, Field field)
    {
        var all = new List<Run>();
        var open = new List<Run>();
        for (var i = 0; i < rows.Count; i++)
        {
            var next = new List<Run>();
            foreach (var seg in Segments(rows[i], field))
            {
                var box = Union(seg);
                var keyed = IsLabel(rows[i].LastOrDefault(w => w.Word.Box.X < seg[0].Word.Box.X)?.Field);
                var cur = open.FirstOrDefault(b => SameLine(b.Last, box));
                var same = cur is not null;
                if (cur is null && !keyed) cur = open.FirstOrDefault(b => OverlapsX(b.Last, box));
                if (cur is null && field == Field.InvoiceDate)
                    cur = open.FirstOrDefault(b => JoinsDate([.. b.Lines.SelectMany(l => l)], seg));
                if (cur is not null) open.Remove(cur);
                else
                {
                    cur = new Run { Row = i, Role = rows[i][0].Role };
                    all.Add(cur);
                }
                if (same) cur.Lines[^1].AddRange(seg);
                else cur.Lines.Add([.. seg]);
                cur.Last = box;
                next.Add(cur);
            }
            open = next;
        }
        foreach (var run in all)
        {
            run.Lines = [.. run.Lines.Select(l => l.OrderBy(w => w.Word.Box.X).ToList())];
            if (field == Field.Supplier) run.Lines = Undouble(run.Lines);
            run.Words = [.. run.Lines.SelectMany(l => l)];
            run.Head = Union(run.Lines[0]);
            run.Text = string.Join(" ", run.Words.Select(w => w.Word.Text));
            run.Conf = run.Words.Count == 0 ? 0 : run.Words.Average(w => (double)w.Conf);
        }
        return all;
    }

    // Split where the gap is a column rather than a word space, in word heights.
    static List<List<TaggedWord>> Segments(List<TaggedWord> row, Field field)
    {
        var out_ = new List<List<TaggedWord>>();
        foreach (var w in row)
        {
            if (w.Field != field) continue;
            var p = out_.Count > 0 ? out_[^1][^1].Word.Box : null;
            if (p is not null && w.Word.Box.X - (p.X + p.W) <= Gap * Math.Max(w.Word.Box.H, p.H))
                out_[^1].Add(w);
            else out_.Add([w]);
        }
        return field == Field.InvoiceDate ? UnsplitDate(out_) : out_;
    }

    static List<List<TaggedWord>> UnsplitDate(List<List<TaggedWord>> segs)
    {
        var out_ = new List<List<TaggedWord>>();
        foreach (var seg in segs)
            if (out_.Count > 0 && JoinsDate(out_[^1], seg)) out_[^1] = [.. out_[^1], .. seg];
            else out_.Add(seg);
        return out_;
    }

    static bool JoinsDate(List<TaggedWord> a, List<TaggedWord> b) =>
        !IsDate(a) && !IsDate(b) && IsDate([.. a, .. b]);

    static bool IsDate(List<TaggedWord> seg) =>
        Parse.Date(string.Join(" ", seg.Select(w => w.Word.Text))) is not null;

    // A wordmark over the sender line prints the name twice: 'Pucher Pucher OG'.
    static List<List<TaggedWord>> Undouble(List<List<TaggedWord>> lines)
    {
        var printings = lines.SelectMany(Printings).ToList();
        var keys = printings.Select(p => p.Select(w => CaseFold(w.Word.Text)).ToList()).ToList();
        var kept = new List<List<TaggedWord>>();
        for (var i = 0; i < printings.Count; i++)
        {
            var drop = false;
            for (var j = 0; j < keys.Count && !drop; j++)
                if (j != i && Inside(keys[i], keys[j])
                    && (string.Concat(keys[j]) != string.Concat(keys[i]) || j > i))
                    drop = true;
            if (!drop) kept.Add(printings[i]);
        }
        return kept;
    }

    static List<List<TaggedWord>> Printings(List<TaggedWord> line)
    {
        var keys = line.Select(w => CaseFold(w.Word.Text)).ToList();
        var best = -1;
        var bestKey = (Agree: -1, Cut: 0);
        for (var i = 1; i < keys.Count; i++)
        {
            var a = keys[..i];
            var b = keys[i..];
            if (!Inside(a, b) && !Inside(b, a)) continue;
            var key = (Agree: Math.Min(i, keys.Count - i), Cut: -i);
            if (best < 0 || key.Agree > bestKey.Agree || (key.Agree == bestKey.Agree && key.Cut > bestKey.Cut))
            {
                best = i;
                bestKey = key;
            }
        }
        return best < 0 ? [line] : [line[..best], line[best..]];
    }

    // Letters run together: 'Trautm ann' is still 'Trautmann'.
    static bool Inside(List<string> a, List<string> b)
    {
        var text = string.Concat(b);
        var want = string.Concat(a);
        var at = 0;
        foreach (var w in b)
        {
            if (at + want.Length <= text.Length && string.CompareOrdinal(text, at, want, 0, want.Length) == 0)
                return true;
            at += w.Length;
        }
        return false;
    }

    static string CaseFold(string s) => s.ToLowerInvariant().Replace("ß", "ss");

    static bool OverlapsX(Box a, Box b) => a.X < b.X + b.W && b.X < a.X + a.W;

    // One printed line that skew split into two rows.
    static bool SameLine(Box a, Box b) =>
        Math.Min(a.Y + a.H, b.Y + b.H) - Math.Max(a.Y, b.Y) > 0.5 * Math.Min(a.H, b.H);

    static List<Box> LabelRuns(List<List<TaggedWord>> rows, Field label) =>
        [.. rows.SelectMany(row => Segments(row, label)).Select(Union)];

    static (int Rank, double Distance)? Tier(Box lab, Run run, double extent)
    {
        var head = run.Head;
        if (SameLine(lab, head) && head.X + head.W / 2.0 > lab.X + lab.W / 2.0)
            return (0, Math.Abs(head.X - (lab.X + lab.W)));
        if (lab.Y < head.Y && head.Y < lab.Y + 2.5 * lab.H
            && (OverlapsX(lab, head) || Math.Abs(head.X - lab.X) <= NearX * extent))
            return (1, Math.Abs((double)head.X - lab.X));
        var dx = head.X + head.W / 2.0 - lab.X - lab.W / 2.0;
        var dy = head.Y + head.H / 2.0 - lab.Y - lab.H / 2.0;
        var d = Math.Sqrt(dx * dx + dy * dy);
        return d <= Radius * extent ? (2, d) : null;
    }

    static Run? Keyed(List<List<TaggedWord>> rows, List<Run> runs, Field label, double extent)
    {
        Run? best = null;
        (int Rank, double Conf, int Y, int X) bestKey = default;
        foreach (var lab in LabelRuns(rows, label))
        {
            Run? claim = null;
            (int Rank, double Distance) claimTier = default;
            foreach (var run in runs)
            {
                if (Tier(lab, run, extent) is not { } t) continue;
                if (claim is null || t.Rank < claimTier.Rank
                    || (t.Rank == claimTier.Rank && t.Distance < claimTier.Distance))
                {
                    claim = run;
                    claimTier = t;
                }
            }
            if (claim is null) continue;
            var key = (Rank: -claimTier.Rank, claim.Conf, Y: -claim.Head.Y, X: -claim.Head.X);
            if (best is null || Greater(key, bestKey))
            {
                best = claim;
                bestKey = key;
            }
        }
        return best;
    }

    static bool Greater((int Rank, double Conf, int Y, int X) a, (int Rank, double Conf, int Y, int X) b)
    {
        if (a.Rank != b.Rank) return a.Rank > b.Rank;
        if (a.Conf != b.Conf) return a.Conf > b.Conf;
        if (a.Y != b.Y) return a.Y > b.Y;
        return a.X > b.X;
    }
}

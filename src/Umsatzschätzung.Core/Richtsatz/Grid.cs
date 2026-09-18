using System.Text;

namespace Umsatzschätzung.Richtsatz;

sealed record Half(Sheet Sheet, List<Word> Words, List<Rule> Rules);

static class Grid
{
    const double ClusterGap = 2.0;

    const double Bund = 8.0;

    // A border is drawn per row of a table, and the shortest rows are the synonym lists.
    const double Strich = 4.0;

    // A landscape sheet carries two printed pages with a gutter between them; the years
    // imposed as an A5 booklet carry one per sheet and must not be cut down the middle of a
    // table, whose columns leave gutters of their own.
    public static IEnumerable<Half> Halves(Sheet sheet)
    {
        var middle = Bundsteg(sheet);
        if (middle is null)
        {
            yield return new Half(sheet, sheet.Words, sheet.Rules);
            yield break;
        }
        yield return new Half(sheet, [.. sheet.Words.Where(w => w.Xc < middle)], [.. sheet.Rules.Where(r => r.X1 <= middle)]);
        yield return new Half(sheet, [.. sheet.Words.Where(w => w.Xc >= middle)], [.. sheet.Rules.Where(r => r.X0 > middle)]);
    }

    static double? Bundsteg(Sheet sheet)
    {
        if (sheet.Width <= sheet.Height) return null;

        // Only the words and the column borders say where a page ends: every sheet carries a
        // rule across its full width at the foot.
        var spans = sheet.Words.Select(w => (w.X0, w.X1))
            .Concat(sheet.Rules.Where(r => r.Width <= 3 && r.Height >= Strich).Select(r => (r.X0, r.X1)))
            .OrderBy(s => s.Item1).ToList();
        double reach = 0, best = 0, at = 0;
        foreach (var (x0, x1) in spans)
        {
            if (reach > 0 && x0 - reach > best) (best, at) = (x0 - reach, (x0 + reach) / 2);
            reach = Math.Max(reach, x1);
        }
        return best >= Bund && Math.Abs(at - sheet.Width / 2) < sheet.Width / 8 ? at : null;
    }

    public static List<List<Word>> Lines(IEnumerable<Word> words)
    {
        var lines = new List<List<Word>>();
        foreach (var word in words.OrderBy(w => w.Baseline).ThenBy(w => w.X0))
        {
            if (lines.Count > 0 && word.Baseline - lines[^1][0].Baseline <= 1.5) lines[^1].Add(word);
            else lines.Add([word]);
        }
        // Words of one line can differ slightly in baseline, so the reading order within it
        // only falls out after the grouping.
        foreach (var line in lines) line.Sort((a, b) => a.X0.CompareTo(b.X0));
        return lines;
    }

    public static string Text(IEnumerable<Word> words) => string.Join(" ", words.Select(w => w.Text));

    public static string Block(IEnumerable<Word> words) => Join(Lines(words).Select(Text));

    // A word broken across two lines keeps its hyphen only sometimes: the Sammlung marks
    // most of them as not being text, and pdfium then drops them. So a line of a cell that
    // starts in lower case continues the word above it, unless it starts with one of these -
    // the whole set of lower case words the table begins a line with.
    static readonly string[] Ganz =
        ["und", "oder", "bzw", "bis", "über", "von", "mit", "u", "versch", "einschl", "sonstigen", "einbezogen", "elektrotechnischen"];

    public static string Join(IEnumerable<string> lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            var gebrochen = line.Length > 0 && char.IsLower(line[0]) && !Ganz.Contains(line.Split(' ')[0].TrimEnd('.', ',', ')', ':'));
            if (text.Length > 0)
            {
                if (gebrochen && text[^1] == '-') text.Length--;
                else if (!gebrochen || !char.IsLower(text[^1])) text.Append(' ');
            }
            text.Append(line);
        }
        return text.ToString();
    }

    // Borders are thin filled rectangles that the writer split into a run of segments per
    // line, so a rule is a cluster of them and its length is what makes it one.
    public static List<(double Pos, double Length)> Verticals(IEnumerable<Rule> rules) =>
        Cluster(rules.Where(r => r.Width <= 3 && r.Height >= Strich).Select(r => ((r.X0 + r.X1) / 2, r.Height)));

    public static List<(double Pos, double Length)> Horizontals(IEnumerable<Rule> rules) =>
        Cluster(rules.Where(r => r.Height <= 3).Select(r => ((r.Top + r.Bottom) / 2, r.Width)));

    public static List<double> Long(List<(double Pos, double Length)> clusters, double share)
    {
        if (clusters.Count == 0) return [];
        var longest = clusters.Max(c => c.Length);
        return [.. clusters.Where(c => c.Length >= share * longest).Select(c => c.Pos)];
    }

    // A column border runs the height of the body but not of the merged header above it, so
    // how long it is says little. What holds is that it is the rule next to a column heading.
    public static List<double>? Around(List<double> rules, List<double> centres, double left, double right)
    {
        var bounds = new List<double>();
        for (var i = 0; i < centres.Count; i++)
        {
            var before = rules.Where(x => x <= centres[i]).ToList();
            var after = rules.Where(x => x >= centres[i]).ToList();
            // Some sheets leave the last column open, so the outermost border may be missing.
            var lower = before.Count > 0 ? before.Max() : i == 0 ? left : double.NaN;
            var upper = after.Count > 0 ? after.Min() : i == centres.Count - 1 ? right : double.NaN;
            if (double.IsNaN(lower) || double.IsNaN(upper)) return null;
            if (bounds.Count == 0) bounds.Add(lower);
            else if (Math.Abs(bounds[^1] - lower) > 0.5) return null;
            bounds.Add(upper);
        }
        return bounds;
    }

    // What divides two rows of the table is drawn inconsistently: a rule the width of the
    // table between some of them, nothing at all between others, and a rule of its own width
    // above every shaded turnover band. So take every structural line as a candidate - which
    // of them begins a Gewerbeklasse is then a question about the row below it.
    public static List<double> Edges(IEnumerable<Rule> rules, double below)
    {
        var breaks = Cluster(rules.Where(r => r.Width <= 3 && r.Height >= Strich)
                .SelectMany(r => new[] { r.Top, r.Bottom }).Select(y => (y, 1.0)))
            .Where(c => c.Length >= 4).Select(c => c.Pos);

        var lines = Horizontals(rules);
        var width = lines.Count == 0 ? 0 : 0.9 * lines.Max(c => c.Length);
        var across = lines.Where(c => c.Length >= width).Select(c => c.Pos);

        return [.. Cluster(breaks.Concat(across).Where(y => y > below).Select(y => (y, 1.0))).Select(c => c.Pos)];
    }

    static List<(double Pos, double Length)> Cluster(IEnumerable<(double Pos, double Length)> parts)
    {
        var sorted = parts.OrderBy(p => p.Pos).ToList();
        var clusters = new List<(double Pos, double Length)>();
        for (var i = 0; i < sorted.Count;)
        {
            double length = 0, weighted = 0, last = sorted[i].Pos;
            for (; i < sorted.Count && sorted[i].Pos - last <= ClusterGap; i++)
            {
                last = sorted[i].Pos;
                length += sorted[i].Length;
                weighted += sorted[i].Pos * sorted[i].Length;
            }
            if (length > 0) clusters.Add((weighted / length, length));
        }
        return clusters;
    }

    public static int Slot(IReadOnlyList<double> bounds, double pos)
    {
        for (var i = 0; i + 1 < bounds.Count; i++)
            if (pos >= bounds[i] && pos < bounds[i + 1]) return i;
        return -1;
    }
}

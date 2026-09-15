using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Umsatzschätzung.Eval;

public sealed record Line(string Name, long Quantity, string UnitCode, long UnitPrice, long LineNet, long Vat);

public sealed record Doc(string Number, string Date, string SupplierName, long NetTotal, long GrossTotal,
    List<Line> Lines);

public sealed record Result(Dictionary<string, bool> Header, int LinesGot, int LinesWant, int LinesMatched,
    Dictionary<string, (int Hit, int Total)> PerField, int CellsWrong, int CellsTotal)
{
    public bool Clean => CellsWrong == 0;
}

// Tier 1 as named in docs/extraction-eval.md.
public static class Score
{
    public static readonly string[] HeaderFields = ["number", "date", "supplierName", "netTotal", "grossTotal"];
    public static readonly string[] LineFields = ["name", "quantity", "unitCode", "unitPrice", "lineNet", "vat"];

    static readonly Regex Legal = new(@"\b(gmbh|co|kg|ag|ohg|gbr|mbh|e\.?k\.?|ug|se|kgaa|ges\.?m\.?b\.?h\.?)\b",
        RegexOptions.IgnoreCase);
    static readonly Regex Space = new(@"\s+");

    public static string Norm(string? s) => Space.Replace((s ?? "").Trim().ToLowerInvariant(), " ");

    public static string NormSupplier(string? s) =>
        Space.Replace(Legal.Replace(Norm(s), "").Replace(".", "").Trim(), " ");

    public static Result One(Doc got, Doc want, bool skipVat)
    {
        var fields = LineFields.Where(f => !skipVat || f != "vat").ToArray();
        int wrong = 0, total = 0;
        var header = new Dictionary<string, bool>();
        foreach (var f in HeaderFields)
        {
            var ok = f switch
            {
                "number" => Norm(got.Number) == Norm(want.Number),
                "date" => Norm(got.Date) == Norm(want.Date),
                "supplierName" => Norm(got.SupplierName) == Norm(want.SupplierName),
                "netTotal" => got.NetTotal == want.NetTotal,
                _ => got.GrossTotal == want.GrossTotal,
            };
            header[f] = ok;
            total++;
            if (!ok) wrong++;
        }
        header["supplierName_fuzzy"] = NormSupplier(got.SupplierName) == NormSupplier(want.SupplierName);

        var pairs = Match(got.Lines, want.Lines);
        var perField = fields.ToDictionary(f => f, _ => (Hit: 0, Total: 0));
        foreach (var (i, j) in pairs)
            foreach (var f in fields)
            {
                var ok = Equal(got.Lines[i], want.Lines[j], f);
                var (hit, count) = perField[f];
                perField[f] = (hit + (ok ? 1 : 0), count + 1);
                total++;
                if (!ok) wrong++;
            }
        var unmatched = want.Lines.Count - pairs.Count;
        wrong += unmatched * fields.Length;
        total += unmatched * fields.Length;

        return new Result(header, got.Lines.Count, want.Lines.Count, pairs.Count, perField, wrong, total);
    }

    static bool Equal(Line a, Line b, string field) => field switch
    {
        "name" => Norm(a.Name) == Norm(b.Name),
        "unitCode" => Norm(a.UnitCode) == Norm(b.UnitCode),
        "quantity" => a.Quantity == b.Quantity,
        "unitPrice" => a.UnitPrice == b.UnitPrice,
        "lineNet" => a.LineNet == b.LineNet,
        _ => a.Vat == b.Vat,
    };

    // Greedy best-first on name similarity plus lineNet agreement. Ties keep generation
    // order, which is what Python's stable sort gives the reference.
    public static List<(int Got, int Want)> Match(List<Line> got, List<Line> want, double threshold = 0.55)
    {
        var candidates = new List<(double S, int I, int J)>(got.Count * want.Count);
        for (var i = 0; i < got.Count; i++)
            for (var j = 0; j < want.Count; j++)
            {
                var s = Similarity.Ratio(Norm(got[i].Name), Norm(want[j].Name));
                if (got[i].LineNet != 0 && got[i].LineNet == want[j].LineNet) s += 0.5;
                candidates.Add((s, i, j));
            }
        var pairs = new List<(int, int)>();
        var usedGot = new HashSet<int>();
        var usedWant = new HashSet<int>();
        foreach (var (s, i, j) in candidates.OrderByDescending(c => c.S))
        {
            if (s < threshold || usedGot.Contains(i) || usedWant.Contains(j)) continue;
            usedGot.Add(i);
            usedWant.Add(j);
            pairs.Add((i, j));
        }
        return pairs;
    }

    public static string Report(IReadOnlyList<Result> results)
    {
        var n = results.Count == 0 ? 1 : results.Count;
        var text = new StringBuilder();
        var header = HeaderFields.Append("supplierName_fuzzy")
            .Select(f => (f, results.Count(r => r.Header[f]) / (double)n));
        var perField = LineFields.Select(f => (f, Ratio(
            results.Sum(r => r.PerField.GetValueOrDefault(f).Hit),
            results.Sum(r => r.PerField.GetValueOrDefault(f).Total))));
        var costs = results.Select(r => r.CellsWrong).ToList();

        text.AppendLine($"invoices            {results.Count}");
        text.AppendLine($"clean rate          {F(results.Count(r => r.Clean) / (double)n)}");
        text.AppendLine($"line precision      {F(Ratio(results.Sum(r => r.LinesMatched), results.Sum(r => r.LinesGot)))}");
        text.AppendLine($"line recall         {F(Ratio(results.Sum(r => r.LinesMatched), results.Sum(r => r.LinesWant)))}");
        text.AppendLine($"correction cost     mean {(costs.Count == 0 ? 0 : costs.Average()).ToString("F1", CultureInfo.InvariantCulture)}  p90 {P90(costs)}");
        text.AppendLine();
        text.AppendLine("header");
        foreach (var (k, v) in header) text.AppendLine($"  {k,-22}{F(v)}");
        text.AppendLine();
        text.AppendLine("line fields");
        foreach (var (k, v) in perField) text.AppendLine($"  {k,-22}{F(v)}");
        return text.ToString().TrimEnd('\n', '\r');
    }

    static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

    static double Ratio(int a, int b) => b == 0 ? 0.0 : a / (double)b;

    static int P90(List<int> xs)
    {
        if (xs.Count == 0) return 0;
        var sorted = xs.Order().ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)Math.Round(0.9 * (sorted.Count - 1), MidpointRounding.ToEven))];
    }
}

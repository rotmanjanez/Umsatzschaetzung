using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Eval;

public sealed record Result(Dictionary<string, bool> Header, int LinesGot, int LinesWant, int LinesMatched,
    Dictionary<string, (int Hit, int Total)> PerField, int CellsWrong, int CellsTotal)
{
    public bool Clean => CellsWrong == 0;
}

// Tier 1 as named in docs/extraction-eval.md. Date and supplier name are bookkeeping, not
// the estimate: measured and reported, scored only with --full.
public static class Score
{
    public static readonly string[] HeaderFields = ["number", "date", "supplierName", "netTotal", "grossTotal"];
    public static readonly string[] CoreHeaderFields = ["number", "netTotal", "grossTotal"];
    public static readonly string[] LineFields = ["name", "quantity", "unitCode", "unitPrice", "lineNet", "vat"];

    static readonly Regex Space = new(@"\s+");

    public static string Norm(string? s) => Space.Replace((s ?? "").Trim().ToLowerInvariant(), " ");

    public static Result One(Invoice got, Invoice want, bool skipVat, bool full)
    {
        var fields = LineFields.Where(f => !skipVat || f != "vat").ToArray();
        var scored = full ? HeaderFields : CoreHeaderFields;
        int wrong = 0, total = 0;
        var header = new Dictionary<string, bool>();
        foreach (var f in HeaderFields)
        {
            var ok = f switch
            {
                "number" => Norm(got.Number) == Norm(want.Number),
                "date" => got.Date == want.Date,
                "supplierName" => Norm(got.SupplierName) == Norm(want.SupplierName),
                "netTotal" => (got.StatedNet ?? got.NetTotal) == (want.StatedNet ?? want.NetTotal),
                _ => (got.StatedGross ?? got.GrossTotal) == (want.StatedGross ?? want.GrossTotal),
            };
            header[f] = ok;
            if (!scored.Contains(f)) continue;
            total++;
            if (!ok) wrong++;
        }

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

    static bool Equal(InvoiceLine a, InvoiceLine b, string field) => field switch
    {
        "name" => Norm(a.Name) == Norm(b.Name),
        "unitCode" => Norm(a.UnitCode) == Norm(b.UnitCode),
        "quantity" => a.Quantity == b.Quantity,
        "unitPrice" => a.UnitPrice == b.UnitPrice,
        "lineNet" => a.LineNet == b.LineNet,
        _ => a.Vat == b.Vat,
    };

    // Greedy best-first on name similarity plus lineNet agreement.
    public static List<(int Got, int Want)> Match(List<InvoiceLine> got, List<InvoiceLine> want, double threshold = 0.55)
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

    public static string Report(IReadOnlyList<Result> results, bool full)
    {
        var n = results.Count == 0 ? 1 : results.Count;
        var text = new StringBuilder();
        var scored = full ? HeaderFields : CoreHeaderFields;
        var costs = results.Select(r => r.CellsWrong).ToList();

        text.AppendLine($"invoices            {results.Count}");
        text.AppendLine($"clean rate          {F(results.Count(r => r.Clean) / (double)n)}");
        text.AppendLine($"line precision      {F(Ratio(results.Sum(r => r.LinesMatched), results.Sum(r => r.LinesGot)))}");
        text.AppendLine($"line recall         {F(Ratio(results.Sum(r => r.LinesMatched), results.Sum(r => r.LinesWant)))}");
        text.AppendLine($"correction cost     mean {(costs.Count == 0 ? 0 : costs.Average()).ToString("F1", CultureInfo.InvariantCulture)}  p90 {P90(costs)}");
        text.AppendLine();
        text.AppendLine("header");
        foreach (var f in HeaderFields)
            text.AppendLine($"  {f + (scored.Contains(f) ? "" : " (not scored)"),-34}{F(results.Count(r => r.Header[f]) / (double)n)}");
        text.AppendLine();
        text.AppendLine("line fields");
        foreach (var f in LineFields)
            text.AppendLine($"  {f,-34}{F(Ratio(results.Sum(r => r.PerField.GetValueOrDefault(f).Hit), results.Sum(r => r.PerField.GetValueOrDefault(f).Total)))}");
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

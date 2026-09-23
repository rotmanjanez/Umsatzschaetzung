using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed record SuggestionRow(string ProductId, string Name);

public static class Assortment
{
    const int SuggestionCount = 5;
    const long Micro = 1_000_000;

    public static HashSet<string> Listed(Case c) =>
        [.. c.Products.Where(p => !p.Disabled).Select(p => p.ProductId)];

    public static async Task<Report?> Calculate(Session session, Case c, RuleSet rs, CancellationToken ct)
    {
        return await Compute(session, WithProducts(c, ListedOnly(c, rs), []), rs, ct);
    }

    public static async Task<List<SuggestionRow>?> Suggest(Session session, Case c, RuleSet rs, Report sold, CancellationToken ct)
    {
        var candidates = Candidates(c, rs);
        var report = await Compute(session, WithProducts(c, Excluded(c, rs, candidates), Fixed(c, sold, candidates)), rs, ct);
        return report is null ? null : Ranked(rs, sold, report, candidates);
    }

    static async Task<Report?> Compute(Session session, Case c, RuleSet rs, CancellationToken ct)
    {
        try
        {
            return await Task.Run(() => Calculation.Run(c, rs), ct);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception e)
        {
            session.Fail("Kalkulation fehlgeschlagen: " + e.Message);
            return null;
        }
    }

    static CaseProduct Copy(CaseProduct p) =>
        new() { ProductId = p.ProductId, GrossPrice = p.GrossPrice, Vat = p.Vat, Disabled = p.Disabled };

    static List<CaseProduct> ListedOnly(Case c, RuleSet rs)
    {
        var products = c.Products.Select(Copy).ToList();
        var known = products.Select(p => p.ProductId).ToHashSet();
        foreach (var id in rs.Products.Keys)
            if (!known.Contains(id)) products.Add(new CaseProduct { ProductId = id, Disabled = true });
        return products;
    }

    static Case WithProducts(Case c, List<CaseProduct> products, List<PinnedPortions> pinned) => new()
    {
        Id = c.Id,
        Label = c.Label,
        PeriodFrom = c.PeriodFrom,
        PeriodTo = c.PeriodTo,
        Taxpayer = c.Taxpayer,
        Declared = c.Declared,
        Inventory = c.Inventory,
        Invoices = c.Invoices,
        Products = products,
        Yields = c.Yields,
        Pinned = pinned,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    static List<string> Candidates(Case c, RuleSet rs)
    {
        var known = c.Products.Select(p => p.ProductId).ToHashSet();
        return [.. rs.Products.Values
            .Where(p => !known.Contains(p.Id) && p.Meta.ValidOn(c.PeriodTo) && Fits(rs, p, c.Taxpayer.Gewerbe))
            .Select(p => p.Id)];
    }

    static bool Fits(RuleSet rs, Product p, string gewerbe) =>
        p.Recipe.TrueForAll(r => !rs.Ingredients.TryGetValue(r.IngredientId, out var ing)
            || !rs.Categories.TryGetValue(ing.CategoryId, out var cat) || cat.Covers(gewerbe));

    static List<CaseProduct> Excluded(Case c, RuleSet rs, List<string> candidates)
    {
        var products = c.Products.Select(Copy).ToList();
        HashSet<string> keep = [.. products.Select(p => p.ProductId), .. candidates];
        foreach (var id in rs.Products.Keys)
            if (!keep.Contains(id)) products.Add(new CaseProduct { ProductId = id, Disabled = true });
        return products;
    }

    static List<PinnedPortions> Fixed(Case c, Report sold, List<string> candidates)
    {
        var listed = Listed(c);
        List<PinnedPortions> pinned = [];
        foreach (var p in sold.Products)
            if (listed.Contains(p.ProductId) && p.Portions > 0)
                pinned.Add(new PinnedPortions { ProductId = p.ProductId, Portions = p.Portions });
        foreach (var id in candidates) pinned.Add(new PinnedPortions { ProductId = id });
        return pinned;
    }

    sealed record Candidate(string Id, string Name, Dictionary<string, long> Amount, double Weight);

    static List<SuggestionRow> Ranked(RuleSet rs, Report sold, Report report, List<string> ids)
    {
        var left = report.Ingredients.ToDictionary(i => i.IngredientId, i => Math.Max(i.Leftover, 0));
        var cost = report.Ingredients.ToDictionary(i => i.IngredientId, i => i.Used > 0 ? i.UsedCost * Micro / i.Used : 0);
        var markups = sold.Markups.Where(m => m.CostOfGoods > 0 && m.RevenueNet > 0).ToDictionary(m => m.Sparte, m => m.Markup);
        var overall = sold.Totals.CalculatedRevenueNet > 0 ? sold.Totals.Markup : 0;

        List<(string Id, string Name, Dictionary<string, long> Amount, string Main)> raw = [];
        foreach (var id in ids)
        {
            var p = rs.Products[id];
            var amount = new Dictionary<string, long>();
            foreach (var r in p.Recipe)
                if (Scale.ToBase(r.Amount, r.Unit) is var a and > 0)
                    amount[r.IngredientId] = amount.GetValueOrDefault(r.IngredientId) + a;
            if (amount.Count == 0 || !amount.Keys.All(left.ContainsKey)) continue;
            var main = amount.MaxBy(x => x.Value * cost.GetValueOrDefault(x.Key)).Key;
            raw.Add((id, p.Name, amount, main));
        }
        var smallest = new Dictionary<string, long>();
        foreach (var r in raw)
            smallest[r.Main] = Math.Min(smallest.GetValueOrDefault(r.Main, long.MaxValue), r.Amount[r.Main]);

        var pool = raw.Select(r =>
        {
            var sparte = rs.Ingredients.TryGetValue(r.Main, out var ing) && rs.Categories.TryGetValue(ing.CategoryId, out var cat)
                ? cat.Sparte : Sparte.Unbestimmt;
            var complexity = 1 + (r.Amount.Count - 1) / 2.0;
            var size = Math.Sqrt((double)smallest[r.Main] / r.Amount[r.Main]);
            var markup = markups.TryGetValue(sparte, out var m) ? m : overall;
            return new Candidate(r.Id, r.Name, r.Amount, complexity * size * (Bp.Full + Math.Max(markup, 0)) / Bp.Full);
        }).ToList();

        List<SuggestionRow> picked = [];
        while (picked.Count < SuggestionCount)
        {
            Candidate? best = null;
            double bestScore = 0;
            long bestPortions = 0;
            foreach (var cd in pool)
            {
                var portions = cd.Amount.Min(x => left[x.Key] / x.Value);
                if (portions <= 0) continue;
                var value = portions * cd.Amount.Sum(x => x.Value * cost.GetValueOrDefault(x.Key)) / (double)Micro;
                var score = value * cd.Weight;
                if (score > bestScore || score == bestScore && best is not null && string.CompareOrdinal(cd.Id, best.Id) < 0)
                    (best, bestScore, bestPortions) = (cd, score, portions);
            }
            if (best is null) break;
            foreach (var (i, a) in best.Amount) left[i] -= a * bestPortions;
            pool.Remove(best);
            picked.Add(new SuggestionRow(best.Id, best.Name));
        }
        return picked;
    }
}

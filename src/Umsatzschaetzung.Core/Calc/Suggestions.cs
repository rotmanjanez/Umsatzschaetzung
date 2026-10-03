using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

public static class Suggestions
{
    const int Count = 5;
    const long Micro = 1_000_000;

    public static List<string> For(Case c, RuleSet rs, IReadOnlySet<string> dismissed)
    {
        var sold = Calculation.Run(c, rs);
        var candidates = Candidates(c, rs, dismissed);
        List<CaseProduct> products = [.. c.Products, .. candidates.Select(id => new CaseProduct { ProductId = id })];
        return Ranked(rs, sold, Calculation.Run(WithProducts(c, products, Fixed(c, sold, candidates)), rs), candidates);
    }

    public static HashSet<string> Listed(Case c) => [.. c.Products.Select(p => p.ProductId)];

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
        NoRevenue = c.NoRevenue,
        CreatedAt = c.CreatedAt,
        UpdatedAt = c.UpdatedAt,
    };

    static List<string> Candidates(Case c, RuleSet rs, IReadOnlySet<string> dismissed)
    {
        var known = Listed(c);
        var sellable = Sellable(rs);
        return [.. rs.Products.Values
            .Where(p => !known.Contains(p.Id) && !dismissed.Contains(p.Id) && p.Meta.ValidOn(c.PeriodTo) && sellable(p) && Fits(rs, p, c.Taxpayer.Gewerbe))
            .Select(p => p.Id)];
    }

    // Sold by the piece. A product without a recipe is sold as it is bought only if nothing is made
    // from it and its category has a Sparte: what goes into another product is an input, and what is
    // in no Sparte, as cleaning, packaging or freight, is not what the business sells.
    public static Func<Product, bool> Sellable(RuleSet rs)
    {
        var parts = Scale.Parts(rs);
        return p => Scale.Of(p) == Unit.Piece
            && (p.Recipe.Count > 0 || !parts.Contains(p.Id) && !string.IsNullOrEmpty(p.CategoryId) && Scale.Goods(rs, p));
    }

    static bool Fits(RuleSet rs, Product p, string gewerbe) =>
        Recipes.Sold(rs, p).TrueForAll(r => !rs.Products.TryGetValue(r.PartId, out var part) || Category(rs, part)?.Covers(gewerbe) != false);

    static Category? Category(RuleSet rs, Product p) =>
        string.IsNullOrEmpty(p.CategoryId) ? null : rs.Categories.GetValueOrDefault(p.CategoryId);

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

    sealed record Candidate(string Id, Dictionary<string, long> Amount, double Weight);

    static List<string> Ranked(RuleSet rs, Report sold, Report report, List<string> ids)
    {
        var left = report.Supply.ToDictionary(i => i.ProductId, i => Math.Max(i.Leftover, 0));
        var cost = report.Supply.ToDictionary(i => i.ProductId, i => i.Used > 0 ? i.UsedCost * Micro / i.Used : 0);
        var markups = sold.Markups.Where(m => m.CostOfGoods > 0 && m.RevenueNet > 0).ToDictionary(m => m.Sparte, m => m.Markup);
        var overall = sold.Totals.CalculatedRevenueNet > 0 ? sold.Totals.Markup : 0;

        List<(string Id, Dictionary<string, long> Amount, string Main)> raw = [];
        foreach (var id in ids)
        {
            var amount = new Dictionary<string, long>();
            foreach (var r in Recipes.Sold(rs, rs.Products[id]))
                if (Scale.ToBase(r.Amount, r.Unit) is var a and > 0)
                    amount[r.PartId] = amount.GetValueOrDefault(r.PartId) + a;
            if (amount.Count == 0 || !amount.Keys.All(left.ContainsKey)) continue;
            var main = amount.MaxBy(x => x.Value * cost.GetValueOrDefault(x.Key)).Key;
            raw.Add((id, amount, main));
        }
        var smallest = new Dictionary<string, long>();
        foreach (var r in raw)
            smallest[r.Main] = Math.Min(smallest.GetValueOrDefault(r.Main, long.MaxValue), r.Amount[r.Main]);

        var pool = raw.Select(r =>
        {
            var sparte = rs.Products.TryGetValue(r.Main, out var main) ? Category(rs, main)?.Sparte ?? Sparte.Unbestimmt : Sparte.Unbestimmt;
            var complexity = 1 + (r.Amount.Count - 1) / 2.0;
            var size = Math.Sqrt((double)smallest[r.Main] / r.Amount[r.Main]);
            var markup = markups.TryGetValue(sparte, out var m) ? m : overall;
            return new Candidate(r.Id, r.Amount, complexity * size * (Bp.Full + Math.Max(markup, 0)) / Bp.Full);
        }).ToList();

        List<string> picked = [];
        while (picked.Count < Count)
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
            picked.Add(best.Id);
        }
        return picked;
    }
}

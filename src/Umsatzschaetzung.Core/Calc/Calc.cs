using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

internal sealed class IngredientUse
{
    public required string IngredientId { get; init; }
    public long Bought { get; set; }
    public long Cost { get; set; }
    public long Used { get; set; }
    public long UsedCost { get; set; }
    public long Sellable { get; set; }
    public long Opening { get; set; }
    public long Closing { get; set; }
    public List<Purchase> Purchases { get; } = [];
    public YieldRule? Yield { get; set; }
    public bool YieldChosen { get; set; }
    public long YieldRate { get; set; }
}

public static class Calculation
{
    public static Report Run(Case c, RuleSet rs)
    {
        var (uses, ex, flags) = Normalize.Run(c, rs);
        Yield.Run(c, rs, uses);
        var allocs = Allocate(c, rs, uses);
        var rep = Revenue.Run(c, rs, allocs, uses);
        rep.Unmapped = ex.Unmapped;
        rep.Unused = ex.Unused;
        rep.Warnings.AddRange(flags);
        rep.Warnings.AddRange(Scale.Conflicts(rs, ex.Unused.Select(l => l.IngredientId)));
        rep.Ingredients = IngredientRows(rs, uses, allocs);
        var s = rep.Totals;
        s.Purchases = c.Invoices.Sum(inv => inv.Lines.Sum(l => l.LineNet));
        s.UnmappedCost = ex.Unmapped.Sum(l => l.LineNet);
        s.UnusedCost = ex.Unused.Sum(l => l.LineNet);
        if (s.Purchases != 0) s.ExcludedShare = (s.UnmappedCost + s.UnusedCost) * Bp.Full / s.Purchases;
        return rep;
    }

    static List<IngredientRow> IngredientRows(RuleSet rs, SortedDictionary<string, IngredientUse> uses, List<Allocation> allocs)
    {
        var leftover = new Dictionary<string, long>();
        HashSet<string> binding = [];
        foreach (var a in allocs)
        {
            foreach (var l in a.Leftover)
                leftover[l.IngredientId] = leftover.GetValueOrDefault(l.IngredientId) + l.Qty;
            binding.UnionWith(a.Binding);
        }
        var rows = new List<IngredientRow>(uses.Count);
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            rows.Add(new IngredientRow
            {
                IngredientId = id,
                Name = Names.Ingredient(rs, id),
                Unit = Scale.Of(rs, id) ?? Unit.Piece,
                Purchases = u.Purchases,
                Opening = u.Opening,
                Closing = u.Closing,
                Bought = u.Bought,
                Cost = u.Cost,
                Used = u.Used,
                UsedCost = u.UsedCost,
                Yield = u.Yield,
                YieldChosen = u.YieldChosen,
                YieldRate = u.YieldRate,
                Sellable = u.Sellable,
                Leftover = leftover.GetValueOrDefault(id),
                Binding = binding.Contains(id),
            });
        }
        return rows;
    }

    internal static Dictionary<string, CaseProduct> CaseProducts(Case c)
    {
        var output = new Dictionary<string, CaseProduct>(c.Products.Count);
        foreach (var p in c.Products) output[p.ProductId] = p;
        return output;
    }

    internal static List<string> EnabledProducts(Case c, RuleSet rs)
    {
        List<string> output = [];
        foreach (var cp in c.Products)
            if (rs.Products.TryGetValue(cp.ProductId, out var p) && p.Meta.ValidOn(c.PeriodTo))
                output.Add(cp.ProductId);
        output.Sort(StringComparer.Ordinal);
        return output;
    }

    static long Capacity(SortedDictionary<string, IngredientUse> uses, string id) =>
        uses.TryGetValue(id, out var u) && u.Sellable > 0 ? u.Sellable : 0;

    internal static HashSet<string> OverdrawnPins(Case c, RuleSet rs, SortedDictionary<string, IngredientUse> uses)
    {
        var demand = new Dictionary<string, long>();
        foreach (var p in c.Pinned)
            if (rs.Products.TryGetValue(p.ProductId, out var prod))
                foreach (var r in prod.Recipe)
                    demand[r.IngredientId] = demand.GetValueOrDefault(r.IngredientId) + Scale.ToBase(r.Amount, r.Unit) * p.Portions;
        HashSet<string> output = [];
        foreach (var p in c.Pinned)
        {
            if (!rs.Products.TryGetValue(p.ProductId, out var prod))
            {
                output.Add(p.ProductId);
                continue;
            }
            foreach (var r in prod.Recipe)
                if (demand[r.IngredientId] > Capacity(uses, r.IngredientId)) output.Add(p.ProductId);
        }
        return output;
    }

    static List<ProductPortions> PortionsList(Dictionary<string, long> portions, Dictionary<string, long> pinned)
    {
        var output = new List<ProductPortions>(portions.Count);
        foreach (var id in portions.Keys.Order(StringComparer.Ordinal))
            output.Add(new ProductPortions { ProductId = id, Portions = portions[id], Pinned = pinned.ContainsKey(id) });
        return output;
    }

    static List<Allocation> Allocate(Case c, RuleSet rs, SortedDictionary<string, IngredientUse> uses)
    {
        List<Allocation> allocs = [];
        var comps = Knapsack.Components(rs, EnabledProducts(c, rs), uses.Keys);
        for (var i = 0; i < comps.Count; i++)
        {
            var comp = comps[i];
            var input = new KnapsackInput();
            foreach (var id in comp.Ingredients) input.Capacity[id] = Capacity(uses, id);
            foreach (var pid in comp.Products)
            {
                var p = rs.Products[pid];
                input.Recipe[pid] = p.Recipe;
                foreach (var r in p.Recipe)
                    if (!input.Capacity.ContainsKey(r.IngredientId))
                        input.Capacity[r.IngredientId] = Capacity(uses, r.IngredientId);
            }
            foreach (var id in input.Capacity.Keys)
                if (uses.TryGetValue(id, out var u) && u.Bought > 0)
                    input.UnitCost[id] = u.Cost * 10_000 / u.Bought;
            foreach (var p in c.Pinned)
                if (input.Recipe.ContainsKey(p.ProductId)) input.Pinned[p.ProductId] = p.Portions;
            KnapsackResult res;
            try
            {
                res = Knapsack.Solve(input);
            }
            catch (Exception e) when (e is OverflowException or InvalidOperationException)
            {
                throw new InvalidOperationException($"allocate component {i}: {e.Message}", e);
            }
            var a = new Allocation
            {
                Component = i,
                Products = PortionsList(res.Portions, input.Pinned),
                Binding = [.. res.Binding],
                Grid = res.Grid,
                States = res.States,
                Approximate = res.Approximate,
            };
            foreach (var id in res.Leftover.Keys.Order(StringComparer.Ordinal))
                a.Leftover.Add(new Leftover { IngredientId = id, Qty = res.Leftover[id] });
            allocs.Add(a);
        }
        return allocs;
    }
}

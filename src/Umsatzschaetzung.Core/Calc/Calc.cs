using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

internal sealed class Stock
{
    public required string ProductId { get; init; }
    public long Bought { get; set; }
    public long Cost { get; set; }
    public long Used { get; set; }
    public long UsedCost { get; set; }
    public long Sellable { get; set; }
    public long Opening { get; set; }
    public long Closing { get; set; }
    public List<Purchase> Purchases { get; } = [];
    public YieldRule? Yield { get; set; }
    public long YieldRate { get; set; }
}

// Die Zuteilung: je Weg die Portionen, die auf ihm entstanden.
internal sealed class Plan
{
    public List<Allocation> Allocations { get; } = [];
    public List<(Column Column, long Portions)> Taken { get; } = [];
    public HashSet<string> Overdrawn { get; } = [];
}

public static class Calculation
{
    public static Report Run(Case c, RuleSet catalog)
    {
        var rs = Recipes.Effective(c, catalog);
        var (uses, ex, flags) = Normalize.Run(c, rs);
        Yield.Run(c, rs, uses);
        var plan = Allocate(c, rs, uses);
        var rep = Revenue.Run(c, rs, plan, uses, ex.Unused);
        rep.Unmapped = ex.Unmapped;
        rep.Unused = ex.Unused;
        rep.Deposits = ex.Deposits;
        rep.NoRevenue = ex.NoRevenue;
        rep.Warnings.AddRange(flags);
        rep.Supply = SupplyRows(rs, uses, plan);
        var s = rep.Totals;
        s.DepositCharged = ex.Deposits.Where(l => l.LineNet > 0).Sum(l => l.LineNet);
        s.DepositRefunded = ex.Deposits.Where(l => l.LineNet < 0).Sum(l => l.LineNet);
        s.Purchases = c.Invoices.Sum(inv => inv.Lines.Sum(l => l.LineNet)) - s.DepositCharged - s.DepositRefunded;
        s.UnmappedCost = ex.Unmapped.Sum(l => l.LineNet);
        s.UnusedCost = ex.Unused.Sum(l => l.LineNet);
        s.NoRevenueCost = ex.NoRevenue.Sum(l => l.LineNet);
        if (s.Purchases != 0) s.ExcludedShare = (s.UnmappedCost + s.NoRevenueCost) * Bp.Full / s.Purchases;
        return rep;
    }

    static List<SupplyRow> SupplyRows(RuleSet rs, SortedDictionary<string, Stock> uses, Plan plan)
    {
        var leftover = new Dictionary<string, long>();
        HashSet<string> binding = [];
        foreach (var a in plan.Allocations)
        {
            foreach (var l in a.Leftover)
                leftover[l.ProductId] = leftover.GetValueOrDefault(l.ProductId) + l.Qty;
            binding.UnionWith(a.Binding);
        }
        var usedBy = new Dictionary<string, List<SupplyUse>>();
        foreach (var (col, portions) in plan.Taken)
        {
            if (portions == 0) continue;
            foreach (var (id, amount) in col.Amount)
            {
                if (!usedBy.TryGetValue(id, out var list)) usedBy[id] = list = [];
                if (list.Find(x => x.ProductId == col.ProductId && x.PerPortion == amount) is { } same) same.Portions += portions;
                else list.Add(new SupplyUse { ProductId = col.ProductId, Name = rs.Products[col.ProductId].Name, Portions = portions, PerPortion = amount });
            }
        }
        foreach (var list in usedBy.Values)
            list.Sort((x, y) => y.Qty.CompareTo(x.Qty));
        var rows = new List<SupplyRow>(uses.Count);
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            rows.Add(new SupplyRow
            {
                ProductId = id,
                Name = Names.Product(rs, id),
                Unit = Names.ProductUnit(rs, id),
                Purchases = u.Purchases,
                Opening = u.Opening,
                Closing = u.Closing,
                Bought = u.Bought,
                Cost = u.Cost,
                Used = u.Used,
                UsedCost = u.UsedCost,
                Yield = u.Yield,
                YieldRate = u.YieldRate,
                Sellable = u.Sellable,
                UsedBy = usedBy.GetValueOrDefault(id) ?? [],
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

    static long Capacity(SortedDictionary<string, Stock> uses, string id) =>
        uses.TryGetValue(id, out var u) && u.Sellable > 0 ? u.Sellable : 0;

    // Ein Produkt ohne Weg behält den, auf dem alles selbst gemacht wird: ohne Bestand bekommt es dort
    // nur über eine Vorgabe Portionen, und die Kalkulation nennt, was fehlt.
    static Dictionary<string, List<Dictionary<string, long>>> Ways(RuleSet rs, List<string> products, SortedDictionary<string, Stock> uses, HashSet<string> cut)
    {
        HashSet<string> stocked = [.. uses.Where(u => u.Value.Sellable > 0).Select(u => u.Key)];
        var output = new Dictionary<string, List<Dictionary<string, long>>>(products.Count);
        foreach (var id in products)
        {
            var p = rs.Products[id];
            var (routes, more) = Routes.Of(rs, p, stocked);
            output[id] = routes.Count > 0 ? routes : [Recipes.Sold(rs, p).ToDictionary(l => l.PartId, l => l.Amount)];
            if (more) cut.Add(id);
        }
        return output;
    }

    static Plan Allocate(Case c, RuleSet rs, SortedDictionary<string, Stock> uses)
    {
        var plan = new Plan();
        var products = EnabledProducts(c, rs);
        HashSet<string> cut = [];
        var ways = Ways(rs, products, uses, cut);
        var comps = Knapsack.Components(products, ways, uses.Keys);
        for (var i = 0; i < comps.Count; i++)
        {
            var comp = comps[i];
            var input = new KnapsackInput();
            foreach (var id in comp.Supply) input.Capacity[id] = Capacity(uses, id);
            foreach (var pid in comp.Products)
                foreach (var route in ways[pid])
                {
                    input.Columns.Add(new Column(pid, route));
                    foreach (var id in route.Keys) input.Capacity.TryAdd(id, Capacity(uses, id));
                }
            foreach (var id in input.Capacity.Keys)
                if (uses.TryGetValue(id, out var u) && u.Bought > 0)
                    input.UnitCost[id] = u.Cost * 10_000 / u.Bought;
            foreach (var p in c.Pinned)
                if (comp.Products.Contains(p.ProductId)) input.Pinned[p.ProductId] = p.Portions;
            KnapsackResult res;
            try
            {
                res = Knapsack.Solve(input);
            }
            catch (Exception e) when (e is OverflowException or InvalidOperationException)
            {
                throw new InvalidOperationException($"allocate component {i}: {e.Message}", e);
            }
            var portions = new SortedDictionary<string, long>(StringComparer.Ordinal);
            for (var k = 0; k < input.Columns.Count; k++)
            {
                var col = input.Columns[k];
                plan.Taken.Add((col, res.Portions[k]));
                portions[col.ProductId] = portions.GetValueOrDefault(col.ProductId) + res.Portions[k];
            }
            var a = new Allocation
            {
                Component = i,
                Products = [.. portions.Where(p => p.Value > 0 || input.Pinned.ContainsKey(p.Key))
                    .Select(p => new ProductPortions { ProductId = p.Key, Portions = p.Value, Pinned = input.Pinned.ContainsKey(p.Key) })],
                Binding = [.. res.Binding],
                Grid = res.Grid,
                States = res.States,
                Approximate = res.Approximate || comp.Products.Exists(cut.Contains),
            };
            foreach (var id in res.Leftover.Keys.Order(StringComparer.Ordinal))
                a.Leftover.Add(new Leftover { ProductId = id, Qty = res.Leftover[id] });
            plan.Allocations.Add(a);
            plan.Overdrawn.UnionWith(Overdrawn(input, res));
        }
        HashSet<string> sold = [.. products];
        foreach (var p in c.Pinned)
            if (!sold.Contains(p.ProductId) && (p.Portions > 0 || !rs.Products.ContainsKey(p.ProductId))) plan.Overdrawn.Add(p.ProductId);
        return plan;
    }

    // Vorgaben, deren Wege zusammen mehr brauchen, als ein Bestand hergibt.
    static IEnumerable<string> Overdrawn(KnapsackInput input, KnapsackResult res)
    {
        var demand = new Dictionary<string, long>();
        for (var k = 0; k < input.Columns.Count; k++)
            if (input.Pinned.ContainsKey(input.Columns[k].ProductId))
                foreach (var (id, a) in input.Columns[k].Amount)
                    demand[id] = demand.GetValueOrDefault(id) + a * res.Portions[k];
        for (var k = 0; k < input.Columns.Count; k++)
        {
            var col = input.Columns[k];
            if (res.Portions[k] > 0 && input.Pinned.ContainsKey(col.ProductId)
                && col.Amount.Keys.Any(id => demand[id] > input.Capacity.GetValueOrDefault(id)))
                yield return col.ProductId;
        }
    }
}

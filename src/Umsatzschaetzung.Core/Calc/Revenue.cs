using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

readonly record struct ProductAllocation(long Portions, bool Pinned, int Component, List<string> Binding);


readonly record struct UnitCost(long Micro)
{
    public const long Scale = 1_000_000;

    public static UnitCost Of(IngredientUse u) => new(u.Used > 0 ? u.UsedCost * Scale / u.Used : 0);
}

internal static class Revenue
{
    static bool PriceMissing(CaseProduct cp) => cp.GrossPrice <= 0;

    // Der Nettopreis einer Portion: der Bruttopreis der Karte ohne die Umsatzsteuer.
    static long UnitNet(CaseProduct cp) =>
        PriceMissing(cp) ? 0 : cp.GrossPrice * Bp.Full / (Bp.Full + cp.Vat);

    // Die Zutaten der Rezeptur, die diese Portionszahl begrenzt haben.
    static List<string> Binding(RuleSet rs, Product p, List<string> binding)
    {
        List<string> output = [];
        foreach (var r in p.Recipe)
            foreach (var b in binding)
                if (b == r.IngredientId) output.Add(rs.Ingredients.TryGetValue(b, out var ing) ? ing.Name : "");
        return output;
    }

    static long PerPortion(Product p, Dictionary<string, UnitCost> cost)
    {
        long micro = 0;
        foreach (var r in p.Recipe)
            micro += Scale.ToBase(r.Amount, r.Unit) * cost.GetValueOrDefault(r.IngredientId).Micro;
        return micro;
    }

    static Sparte Division(RuleSet rs, Product p, Dictionary<string, UnitCost> cost)
    {
        long best = 0;
        var sparte = Sparte.Unbestimmt;
        foreach (var r in p.Recipe)
        {
            var share = Scale.ToBase(r.Amount, r.Unit) * cost.GetValueOrDefault(r.IngredientId).Micro;
            if (share <= best) continue;
            best = share;
            sparte = rs.Ingredients.TryGetValue(r.IngredientId, out var ing)
                && rs.Categories.TryGetValue(ing.CategoryId, out var cat) ? cat.Sparte : Sparte.Unbestimmt;
        }
        return sparte;
    }

    static List<Flag> Undivided(List<MarkupRow> markups)
    {
        var m = markups.Find(m => m.Sparte == Sparte.Unbestimmt);
        if (m is null || m.RevenueNet == 0) return [];
        return [new Flag
        {
            Code = "sparte-missing",
            Message = $"{Format.Cents(m.RevenueNet)} Umsatz entfallen auf Produkte ohne Sparte; "
                + "ihr Aufschlagsatz steht in keiner Sparte",
        }];
    }

    // Nur die Gastronomie trennt nach Sparten; sonst ist der Satz des Betriebs der einzige.
    static List<MarkupRow> Markups(List<ProductRow> rows, string? kennzahl)
    {
        if (!Gewerbe.Gastronomie(kennzahl)) return [];
        var bySparte = new SortedDictionary<Sparte, MarkupRow>();
        foreach (var row in rows)
        {
            if (row.Portions == 0) continue;
            if (!bySparte.TryGetValue(row.Sparte, out var m)) bySparte[row.Sparte] = m = new MarkupRow { Sparte = row.Sparte };
            m.Portions += row.Portions;
            m.CostOfGoods += row.CostOfGoods;
            m.RevenueNet += row.RevenueNet;
        }
        // Getränke, Speisen und Handelsware zuerst, die Produkte ohne Sparte zuletzt.
        return [.. bySparte.Values.OrderBy(m => m.Sparte == Sparte.Unbestimmt ? 1 : 0)];
    }

    internal static Report Run(Case c, RuleSet rs, List<Allocation> allocs, SortedDictionary<string, IngredientUse> uses)
    {
        var byProduct = new Dictionary<string, ProductAllocation>();
        foreach (var a in allocs)
            foreach (var pp in a.Products)
                byProduct[pp.ProductId] = new(pp.Portions, pp.Pinned, a.Component, a.Binding);
        var pinReasons = new Dictionary<string, string>();
        foreach (var p in c.Pinned) pinReasons[p.ProductId] = p.Reason;
        var settings = Calculation.CaseProducts(c);
        var unitCost = new Dictionary<string, UnitCost>(uses.Count);
        foreach (var (id, u) in uses) unitCost[id] = UnitCost.Of(u);
        long total = 0, portions = 0, allocated = 0;
        List<ProductRow> rows = [];
        List<Flag> flags = [];
        foreach (var pid in rs.Products.Keys.Order(StringComparer.Ordinal))
        {
            var p = rs.Products[pid];
            if (!p.Meta.ValidOn(c.PeriodTo)) continue;
            var known = settings.TryGetValue(pid, out var s);
            var hasPortions = byProduct.TryGetValue(pid, out var pa);
            if (!hasPortions && !known && !pinReasons.ContainsKey(pid)) continue;
            var cp = known ? s! : new CaseProduct();
            var perPortion = PerPortion(p, unitCost);
            var row = new ProductRow
            {
                ProductId = pid,
                Name = p.Name,
                UnitNet = UnitNet(cp),
                PinReason = pinReasons.GetValueOrDefault(pid, ""),
                Sparte = Division(rs, p, unitCost),
                CostPerPortion = perPortion / UnitCost.Scale,
                GrossPrice = cp.GrossPrice,
                Vat = cp.Vat,
                Disabled = cp.Disabled,
                PriceMissing = PriceMissing(cp),
            };
            if (hasPortions)
            {
                var net = pa.Portions * UnitNet(cp);
                total += net;
                portions += pa.Portions;
                row.Binding = Binding(rs, p, pa.Binding);
                (row.Portions, row.Pinned, row.RevenueNet) = (pa.Portions, pa.Pinned, net);
                row.CostOfGoods = pa.Portions * perPortion / UnitCost.Scale;
                allocated += row.CostOfGoods;
                if (row.PriceMissing)
                    flags.Add(new Flag { Code = "price_missing", Message = $"Preis fehlt für „{p.Name}“ ({Format.Portions(pa.Portions)} ohne Umsatz)" });
            }
            rows.Add(row);
        }
        var markups = Markups(rows, c.Taxpayer.Gewerbe);

        long cost = 0, stock = 0, sellable = 0;
        foreach (var u in uses.Values)
        {
            cost += u.UsedCost;
            stock += u.Cost - u.UsedCost;
            sellable += u.Used > 0 ? u.UsedCost * u.Sellable / u.Used : 0;
        }
        var summary = new Totals
        {
            CalculatedRevenueNet = total,
            CostOfGoods = cost,
            StockChange = stock,
            SellableCost = sellable,
            AllocatedCost = allocated,
            Portions = portions,
        };
        return new Report
        {
            CaseId = c.Id,
            ComputedAt = DateTimeOffset.UtcNow,
            Totals = summary,
            Products = rows,
            Markups = markups,
            Allocations = allocs,
            Warnings = [.. Warnings(c, rs, uses, allocs), .. flags, .. Undivided(markups)],
        };
    }

    static List<Flag> Warnings(Case c, RuleSet rs, SortedDictionary<string, IngredientUse> uses, List<Allocation> allocs)
    {
        List<Flag> output = [];
        foreach (var id in uses.Keys)
        {
            var name = rs.Ingredients.TryGetValue(id, out var ing) ? ing.Name : "";
            if (uses[id].Used < 0)
                output.Add(new Flag { Code = "usage-negative", Message = $"Verbrauch von „{name}“ ist negativ (Endbestand größer als Anfangsbestand + Einkauf)" });
        }
        foreach (var y in c.Yields)
            if (!rs.YieldRules.ContainsKey(y.YieldRuleId))
                output.Add(new Flag { Code = "yield-choice-unknown", Message = $"Gewählte Ertragsregel \"{y.YieldRuleId}\" existiert nicht, es gilt die Standardregel" });
        var overdrawn = new List<string>(Calculation.OverdrawnPins(c, rs, uses));
        overdrawn.Sort(StringComparer.Ordinal);
        foreach (var id in overdrawn)
        {
            var name = rs.Products.TryGetValue(id, out var p) ? p.Name : id;
            output.Add(new Flag { Code = "pinned-overdrawn", Message = $"Vorgabe für „{name}“ übersteigt die verfügbaren Mengen; der Rest dieser Zutaten wurde auf null gesetzt" });
        }
        foreach (var a in allocs)
            if (a.Approximate)
                output.Add(new Flag { Code = "allocation-approximate", Message = $"Komponente {a.Component + 1} wurde näherungsweise gelöst" });
        return output;
    }
}

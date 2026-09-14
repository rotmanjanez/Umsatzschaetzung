using Umsatzschätzung.Model;

namespace Umsatzschätzung.Calc;

readonly record struct ProductAllocation(long Portions, bool Pinned, int Component, List<string> Binding);

internal static class Revenue
{
    static bool PriceMissing(CaseProduct cp) => cp.GrossPrice <= 0;

    static (long Net, string Formula) ProductNet(CaseProduct cp, long portions)
    {
        if (PriceMissing(cp))
            return (0, $"{Format.Portions(portions)} × Preis fehlt = {Format.Cents(0)}");
        var unitNet = cp.GrossPrice * Bp.Full / (Bp.Full + cp.Vat);
        var net = portions * unitNet;
        return (net, $"{Format.Cents(cp.GrossPrice)} brutto ÷ (100 % + {Format.Bp(cp.Vat)} USt) = {Format.Cents(unitNet)} netto je Portion; {Format.Portions(portions)} × {Format.Cents(unitNet)} = {Format.Cents(net)}");
    }

    static Node ProductNode(Case c, RuleSet rs, Product p, CaseProduct cp, ProductAllocation pa, SortedDictionary<string, IngredientUse> uses, string pinReason)
    {
        var (net, formula) = ProductNet(cp, pa.Portions);
        var portionNode = new Node
        {
            Label = p.Name + ": Portionen",
            Value = pa.Portions,
            Unit = ValueUnit.Portion,
            Sources = [new SourceRef { Kind = SourceKind.Allocation, Entity = Entity.Product, EntityId = p.Id, Reason = $"Komponente {pa.Component + 1}" }],
        };
        List<string> binding = [];
        foreach (var r in p.Recipe)
        {
            if (uses.TryGetValue(r.IngredientId, out var u)) portionNode.Inputs.Add(u.Node!);
            foreach (var b in pa.Binding)
                if (b == r.IngredientId) binding.Add(rs.Ingredients.TryGetValue(b, out var ing) ? ing.Name : "");
        }
        if (pa.Pinned)
        {
            portionNode.Formula = $"vorgegeben: {Format.Portions(pa.Portions)}";
            portionNode.Sources.Add(new SourceRef { Kind = SourceKind.Pinned, Entity = Entity.Product, EntityId = p.Id, Reason = pinReason });
        }
        else if (binding.Count > 0)
            portionNode.Formula = $"Zuteilung, begrenzt durch {string.Join(", ", binding)} = {Format.Portions(pa.Portions)}";
        else
            portionNode.Formula = "Zuteilung = " + Format.Portions(pa.Portions);
        var priceNode = new Node
        {
            Label = p.Name + ": Preis (brutto)",
            Value = cp.GrossPrice,
            Unit = ValueUnit.Eur,
            Formula = PriceMissing(cp) ? "Preis fehlt" : $"{Format.Cents(cp.GrossPrice)} brutto, {Format.Bp(cp.Vat)} USt",
            Sources = [new SourceRef { Kind = SourceKind.Case, EntityId = c.Id, Reason = "Preis der Prüfung" }],
        };
        return new Node
        {
            Label = p.Name + ": Umsatz (netto)",
            Value = net,
            Unit = ValueUnit.Eur,
            Formula = formula,
            Inputs = [portionNode, priceNode],
        };
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
        var root = new Node
        {
            Label = "Kalkulierter Umsatz (netto)",
            Unit = ValueUnit.Eur,
            Sources = [new SourceRef { Kind = SourceKind.Case, EntityId = c.Id, Reason = "Nettopreis je Portion abgerundet" }],
        };
        long total = 0, portions = 0;
        List<ProductRow> rows = [];
        List<Flag> flags = [];
        foreach (var pid in rs.Products.Keys.Order(StringComparer.Ordinal))
        {
            var p = rs.Products[pid];
            if (!p.Meta.ValidOn(c.PeriodTo)) continue;
            var cp = settings.TryGetValue(pid, out var s) ? s : new CaseProduct();
            var row = new ProductRow { ProductId = pid, GrossPrice = cp.GrossPrice, Vat = cp.Vat, Disabled = cp.Disabled, PriceMissing = PriceMissing(cp) };
            if (byProduct.TryGetValue(pid, out var pa))
            {
                var n = ProductNode(c, rs, p, cp, pa, uses, pinReasons.GetValueOrDefault(pid, ""));
                root.Inputs.Add(n);
                total += n.Value;
                portions += pa.Portions;
                (row.Portions, row.Pinned, row.RevenueNet) = (pa.Portions, pa.Pinned, n.Value);
                if (row.PriceMissing)
                    flags.Add(new Flag { Code = "price_missing", Message = $"Preis fehlt für „{p.Name}“ ({Format.Portions(pa.Portions)} ohne Umsatz)" });
            }
            rows.Add(row);
        }
        root.Value = total;
        root.Formula = $"Summe über {root.Inputs.Count} Produkte = {Format.Cents(total)}";

        long cost = 0, stock = 0;
        foreach (var u in uses.Values)
        {
            cost += u.UsedCost;
            stock += u.Cost - u.UsedCost;
        }
        var summary = new Totals
        {
            CalculatedRevenueNet = total,
            CostOfGoods = cost,
            StockChange = stock,
            GrossProfit = total - cost,
            Portions = portions,
        };
        if (cost != 0) summary.Markup = summary.GrossProfit * Bp.Full / cost;
        return new Report
        {
            CaseId = c.Id,
            ComputedAt = DateTimeOffset.UtcNow,
            Totals = summary,
            Root = root,
            Products = rows,
            Allocations = allocs,
            Warnings = [.. Warnings(c, rs, uses, allocs), .. flags],
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

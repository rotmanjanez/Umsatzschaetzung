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
            sparte = SparteOf(rs, r.IngredientId);
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
            if (row.Portions == 0 || row.PriceMissing) continue;
            if (!bySparte.TryGetValue(row.Sparte, out var m)) bySparte[row.Sparte] = m = new MarkupRow { Sparte = row.Sparte };
            m.Portions += row.Portions;
            m.CostOfGoods += row.CostOfGoods;
            m.RevenueNet += row.RevenueNet;
        }
        // Getränke, Speisen und Handelsware zuerst, die Produkte ohne Sparte zuletzt.
        return [.. bySparte.Values.OrderBy(m => m.Sparte == Sparte.Unbestimmt ? 1 : 0)];
    }

    static Sparte SparteOf(RuleSet rs, string ingredientId) =>
        rs.Ingredients.TryGetValue(ingredientId, out var ing) && rs.Categories.TryGetValue(ing.CategoryId, out var cat) ? cat.Sparte : Sparte.Unbestimmt;

    static List<EstimateRow> Estimates(Case c, RuleSet rs, List<ProductRow> rows, List<Allocation> allocs,
        Dictionary<string, UnitCost> unitCost, List<UnusedLine> unused)
    {
        List<EstimateRow> output = [];
        foreach (var row in rows)
            if (row.PriceMissing && row.Portions > 0)
                output.Add(new EstimateRow { Source = EstimateSource.PriceMissing, Name = row.Name, Unit = Unit.Piece, Qty = row.Portions, Basis = row.Sparte, Cost = row.CostOfGoods });
        var leftover = new SortedDictionary<string, long>(StringComparer.Ordinal);
        foreach (var a in allocs)
            foreach (var l in a.Leftover)
                leftover[l.IngredientId] = leftover.GetValueOrDefault(l.IngredientId) + l.Qty;
        foreach (var (id, qty) in leftover)
            if (qty * unitCost.GetValueOrDefault(id).Micro / UnitCost.Scale is var cost and not 0)
                output.Add(new EstimateRow { Source = EstimateSource.Leftover, Name = Names.Ingredient(rs, id), Unit = Scale.Of(rs, id) ?? Unit.Piece, Qty = qty, Basis = SparteOf(rs, id), Cost = cost });
        var invoices = c.Invoices.ToDictionary(i => i.Id);
        foreach (var l in unused)
        {
            var inv = invoices.GetValueOrDefault(l.InvoiceId);
            output.Add(new EstimateRow
            {
                Source = EstimateSource.Unused,
                Name = l.Name,
                Invoice = inv is null ? "" : Names.Invoice(inv),
                Date = inv?.Date,
                Basis = SparteOf(rs, l.IngredientId),
                Cost = l.LineNet,
            });
        }
        return output;
    }

    // Die Sparte trägt ihren eigenen Satz, sobald er sich an Portionen mit Preis ermitteln ließ;
    // sonst gilt der Satz des Betriebs.
    static List<Flag> Price(List<EstimateRow> estimates, List<MarkupRow> markups, Totals t)
    {
        var rates = markups.Where(m => m.Sparte != Sparte.Unbestimmt && m.CostOfGoods > 0 && m.RevenueNet > 0).ToDictionary(m => m.Sparte, m => m.Markup);
        var overall = t.PricedCost > 0 && t.CalculatedRevenueNet > 0;
        long missing = 0;
        foreach (var e in estimates)
        {
            if (!rates.TryGetValue(e.Basis, out var markup)) (e.Basis, markup) = (Sparte.Unbestimmt, t.Markup);
            t.EstimatedCost += e.Cost;
            if (e.Basis == Sparte.Unbestimmt && !overall)
            {
                missing += e.Cost;
                continue;
            }
            e.Markup = markup;
            e.RevenueNet = e.Cost + e.Cost * markup / Bp.Full;
            t.EstimatedRevenueNet += e.RevenueNet;
        }
        if (missing == 0) return [];
        return [new Flag
        {
            Code = "markup-missing",
            Message = $"Kein Aufschlagsatz ermittelt, weil keine Portion einen Preis hat; {Format.Cents(missing)} Einsatz bleiben ohne Umsatz",
        }];
    }

    internal static Report Run(Case c, RuleSet rs, RuleSet catalog, List<Allocation> allocs, SortedDictionary<string, IngredientUse> uses, List<UnusedLine> unused)
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
        long total = 0, portions = 0, allocated = 0, priced = 0, pricedPortions = 0;
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
                PriceMissing = PriceMissing(cp),
                RecipeAdjusted = cp.Recipe is not null,
                RecipeBasis = cp.RecipeBasis,
                RecipeStale = Recipes.Stale(cp, catalog),
                CatalogRecipe = cp.Recipe is null ? [] : catalog.Products[pid].Recipe,
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
                    flags.Add(new Flag { Code = "price_missing", Message = $"Preis fehlt für „{p.Name}“ ({Format.Portions(pa.Portions)}, Umsatz über den Aufschlagsatz geschätzt)" });
                else
                {
                    priced += row.CostOfGoods;
                    pricedPortions += pa.Portions;
                }
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
            PricedCost = priced,
            PricedPortions = pricedPortions,
            Portions = portions,
        };
        var estimated = Estimates(c, rs, rows, allocs, unitCost, unused);
        flags.AddRange(Price(estimated, markups, summary));
        return new Report
        {
            CaseId = c.Id,
            ComputedAt = DateTimeOffset.UtcNow,
            Totals = summary,
            Products = rows,
            Markups = markups,
            Estimated = estimated,
            Allocations = allocs,
            Warnings = [.. Warnings(c, rs, uses), .. flags, .. Undivided(markups)],
        };
    }

    static List<Flag> Warnings(Case c, RuleSet rs, SortedDictionary<string, IngredientUse> uses)
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
        return output;
    }
}

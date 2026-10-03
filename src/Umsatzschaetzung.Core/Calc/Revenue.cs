using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

readonly record struct ProductAllocation(long Portions, bool Pinned, int Component, List<string> Binding);


readonly record struct UnitCost(long Micro)
{
    public const long Scale = 1_000_000;

    public static UnitCost Of(Stock u) => new(u.Used > 0 ? u.UsedCost * Scale / u.Used : 0);
}

internal static class Revenue
{
    static bool PriceMissing(CaseProduct cp) => cp.GrossPrice <= 0;

    // Der Nettopreis einer Portion: der Bruttopreis der Karte ohne die Umsatzsteuer.
    static long UnitNet(CaseProduct cp) =>
        PriceMissing(cp) ? 0 : InvoiceMath.RoundDiv(cp.GrossPrice * Bp.Full, Bp.Full + cp.Vat);

    // Die Bestände auf den Wegen des Produkts, die diese Portionszahl begrenzt haben.
    static List<string> Binding(RuleSet rs, List<(Column Column, long Portions)> ways, List<string> binding)
    {
        List<string> output = [];
        foreach (var id in ways.SelectMany(w => w.Column.Amount.Keys).Distinct())
            if (binding.Contains(id)) output.Add(rs.Products.TryGetValue(id, out var part) ? part.Name : "");
        return output;
    }

    static long PerPortion(Column route, Dictionary<string, UnitCost> cost)
    {
        long micro = 0;
        foreach (var (id, amount) in route.Amount)
            micro += amount * cost.GetValueOrDefault(id).Micro;
        return micro;
    }

    // Die eigene Kategorie, sonst der teuerste Bestand auf dem Weg, der die meisten Portionen trug.
    static Sparte Division(RuleSet rs, Product p, Column route, Dictionary<string, UnitCost> cost)
    {
        if (p.CategoryId is not null) return SparteOf(rs, p.Id);
        long best = 0;
        var sparte = Sparte.Unbestimmt;
        foreach (var (id, amount) in route.Amount)
        {
            var share = amount * cost.GetValueOrDefault(id).Micro;
            if (share <= best) continue;
            best = share;
            sparte = SparteOf(rs, id);
        }
        return sparte;
    }

    static List<RouteRow> RouteRows(RuleSet rs, List<(Column Column, long Portions)> used, Dictionary<string, UnitCost> cost) =>
        used.Count < 2 ? [] : [.. used.Select(w => new RouteRow
        {
            Portions = w.Portions,
            CostPerPortion = PerPortion(w.Column, cost) / UnitCost.Scale,
            Parts = [.. w.Column.Amount.Select(a => new RoutePart { ProductId = a.Key, Name = Names.Product(rs, a.Key), Unit = Names.ProductUnit(rs, a.Key), PerPortion = a.Value })],
        })];

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

    static Sparte SparteOf(RuleSet rs, string productId) =>
        rs.Products.TryGetValue(productId, out var p) && p.CategoryId is { } id && rs.Categories.TryGetValue(id, out var cat) ? cat.Sparte : Sparte.Unbestimmt;

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
                leftover[l.ProductId] = leftover.GetValueOrDefault(l.ProductId) + l.Qty;
        foreach (var (id, qty) in leftover)
            if (qty * unitCost.GetValueOrDefault(id).Micro / UnitCost.Scale is var cost and not 0)
                output.Add(new EstimateRow { Source = EstimateSource.Leftover, Name = Names.Product(rs, id), Unit = Names.ProductUnit(rs, id), Qty = qty, Basis = SparteOf(rs, id), Cost = cost });
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
                Basis = SparteOf(rs, l.ProductId),
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

    internal static Report Run(Case c, RuleSet rs, Plan plan, SortedDictionary<string, Stock> uses, List<UnusedLine> unused)
    {
        var allocs = plan.Allocations;
        var byProduct = new Dictionary<string, ProductAllocation>();
        var taken = plan.Taken.GroupBy(t => t.Column.ProductId).ToDictionary(g => g.Key, g => g.ToList());
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
            var ways = taken.GetValueOrDefault(pid) ?? [(new Column(pid, Recipes.Sold(rs, p).ToDictionary(l => l.PartId, l => l.Amount)), 0L)];
            var used = ways.FindAll(w => w.Portions > 0);
            var main = used.Count > 0 ? used.MaxBy(w => w.Portions).Column : ways[0].Column;
            long micro = 0, count = 0;
            foreach (var (route, n) in used)
            {
                micro += n * PerPortion(route, unitCost);
                count += n;
            }
            var row = new ProductRow
            {
                ProductId = pid,
                Name = p.Name,
                UnitNet = UnitNet(cp),
                PinReason = pinReasons.GetValueOrDefault(pid, ""),
                Sparte = Division(rs, p, main, unitCost),
                CostPerPortion = (count > 0 ? micro / count : PerPortion(main, unitCost)) / UnitCost.Scale,
                Routes = RouteRows(rs, used, unitCost),
                GrossPrice = cp.GrossPrice,
                Vat = cp.Vat,
                PriceMissing = PriceMissing(cp),
            };
            if (hasPortions)
            {
                var net = pa.Portions * UnitNet(cp);
                total += net;
                portions += pa.Portions;
                row.Binding = Binding(rs, ways, pa.Binding);
                (row.Portions, row.Pinned, row.RevenueNet) = (pa.Portions, pa.Pinned, net);
                row.CostOfGoods = micro / UnitCost.Scale;
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
            Warnings = [.. Warnings(c, rs, uses, plan.Overdrawn), .. Approximate(rs, allocs), .. flags, .. Undivided(markups)],
        };
    }

    // Zu viele gemeinsame Bestände oder Wege für die genaue Rechnung: die Portionen sind gierig verteilt.
    static IEnumerable<Flag> Approximate(RuleSet rs, List<Allocation> allocs) =>
        allocs.Where(a => a.Approximate && a.Products.Count > 0).Select(a =>
        {
            var names = a.Products.Select(p => $"„{Names.Product(rs, p.ProductId)}“").ToList();
            var who = names.Count <= 3 ? string.Join(", ", names) : string.Join(", ", names.Take(3)) + $" und {names.Count - 3} weitere";
            return new Flag { Code = "approximate", Message = $"Die Portionen von {who} sind näherungsweise verteilt; für die genaue Rechnung teilen sie zu viele Bestände oder Wege" };
        });

    static List<Flag> Warnings(Case c, RuleSet rs, SortedDictionary<string, Stock> uses, HashSet<string> overdrawnPins)
    {
        List<Flag> output = [];
        foreach (var id in uses.Keys)
        {
            var name = rs.Products.TryGetValue(id, out var p) ? p.Name : "";
            if (uses[id].Used < 0)
                output.Add(new Flag { Code = "usage-negative", Message = $"Verbrauch von „{name}“ ist negativ (Endbestand größer als Anfangsbestand + Einkauf)" });
        }
        foreach (var y in c.Yields)
            if (y.YieldRuleId is { } id && !rs.YieldRules.ContainsKey(id))
                output.Add(new Flag { Code = "yield-choice-unknown", Message = $"Gewählte Ertragsregel \"{y.YieldRuleId}\" existiert nicht mehr, es gilt die Standardregel" });
        var overdrawn = new List<string>(overdrawnPins);
        overdrawn.Sort(StringComparer.Ordinal);
        foreach (var id in overdrawn)
        {
            var name = rs.Products.TryGetValue(id, out var p) ? p.Name : id;
            output.Add(new Flag { Code = "pinned-overdrawn", Message = $"Vorgabe für „{name}“ übersteigt die verfügbaren Mengen; der Rest dieser Bestände wurde auf null gesetzt" });
        }
        return output;
    }
}

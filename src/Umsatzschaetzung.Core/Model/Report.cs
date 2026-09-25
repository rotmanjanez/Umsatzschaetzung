using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Model;

// Rohgewinnaufschlagsatz in Basispunkten: was der Verkauf über den Wareneinsatz hinaus
// einbringt. Ohne Einsatz gibt es keinen Satz.
public static class Rohaufschlag
{
    public static long Of(long revenueNet, long costOfGoods) =>
        costOfGoods > 0 ? (revenueNet - costOfGoods) * Bp.Full / costOfGoods : 0;
}

public sealed class Totals
{
    public long CalculatedRevenueNet { get; set; }
    public long Purchases { get; set; }
    public long CostOfGoods { get; set; }
    public long StockChange { get; set; }
    // Der Wareneinsatz wandert über zwei Abzüge in den Umsatz: die Ertragsregeln nehmen die
    // nicht verkaufsfähige Menge heraus, die Zuteilung lässt den Rest übrig.
    public long SellableCost { get; set; }
    public long AllocatedCost { get; set; }
    public long ShrinkageCost => CostOfGoods - SellableCost;
    public long UnallocatedCost => SellableCost - AllocatedCost;
    // Der Aufschlagsatz wird nur an Portionen mit Preis ermittelt.
    public long PricedCost { get; set; }
    public long PricedPortions { get; set; }
    public long GrossProfit => CalculatedRevenueNet - PricedCost;
    public long Markup => Rohaufschlag.Of(CalculatedRevenueNet, PricedCost);
    public long EstimatedCost { get; set; }
    public long EstimatedRevenueNet { get; set; }
    public long RevenueNet => CalculatedRevenueNet + EstimatedRevenueNet;
    public long Portions { get; set; }
    public long UnmappedCost { get; set; }
    public long UnusedCost { get; set; }
    public long NoRevenueCost { get; set; }
    public long DepositCharged { get; set; }
    public long DepositRefunded { get; set; }
    public long ExcludedShare { get; set; }
}

// Umsatz je Steuersatz vor und nach der Prüfung, zuletzt die Summe über alle Sätze.
public sealed record VatRow(long Vat, long Declared, long Calculated, bool Total)
{
    public long Difference => Calculated - Declared;

    static readonly long[] Rates = [1900, 700, 0];

    public static List<VatRow> Of(Case c, Report r)
    {
        var declared = new Dictionary<long, long>();
        foreach (var d in c.Declared) declared[d.Vat] = declared.GetValueOrDefault(d.Vat) + d.Net;
        var calculated = new Dictionary<long, long>();
        foreach (var p in r.Products)
            calculated[p.Vat] = calculated.GetValueOrDefault(p.Vat) + p.RevenueNet;
        foreach (var (vat, net) in Estimated(r))
            calculated[vat] = calculated.GetValueOrDefault(vat) + net;
        List<VatRow> rows = [];
        long sumDeclared = 0, sumCalculated = 0;
        foreach (var rate in Rates.Concat(declared.Keys.Union(calculated.Keys).Where(k => !Rates.Contains(k)).Order()))
        {
            var d = declared.GetValueOrDefault(rate);
            var k = calculated.GetValueOrDefault(rate);
            if (d == 0 && k == 0) continue;
            sumDeclared += d;
            sumCalculated += k;
            rows.Add(new VatRow(rate, d, k, false));
        }
        rows.Add(new VatRow(0, sumDeclared, sumCalculated, true));
        return rows;
    }

    // Der geschätzte Umsatz folgt den Steuersätzen des Umsatzes, dessen Aufschlagsatz er trägt.
    static Dictionary<long, long> Estimated(Report r)
    {
        var byBasis = new Dictionary<Sparte, long>();
        foreach (var e in r.Estimated) byBasis[e.Basis] = byBasis.GetValueOrDefault(e.Basis) + e.RevenueNet;
        var output = new Dictionary<long, long>();
        foreach (var (basis, amount) in byBasis)
        {
            var weights = new Dictionary<long, long>();
            foreach (var p in r.Products)
                if (!p.PriceMissing && (basis == Sparte.Unbestimmt || p.Sparte == basis))
                    weights[p.Vat] = weights.GetValueOrDefault(p.Vat) + p.RevenueNet;
            var sum = weights.Values.Sum();
            if (sum <= 0) continue;
            var order = weights.OrderByDescending(w => w.Value).ThenByDescending(w => w.Key).ToList();
            var left = amount;
            foreach (var (vat, weight) in order.Skip(1))
            {
                var part = (long)((Int128)amount * weight / sum);
                output[vat] = output.GetValueOrDefault(vat) + part;
                left -= part;
            }
            output[order[0].Key] = output.GetValueOrDefault(order[0].Key) + left;
        }
        return output;
    }
}

public sealed class UnmappedLine
{
    public string InvoiceId { get; set; } = "";
    public long LineNo { get; set; }
    public string Name { get; set; } = "";
    public long LineNet { get; set; }
}

public sealed class UnusedLine
{
    public string InvoiceId { get; set; } = "";
    public long LineNo { get; set; }
    public string Name { get; set; } = "";
    public long LineNet { get; set; }
    public string IngredientId { get; set; } = "";
}

public enum EstimateSource
{
    [JsonStringEnumMemberName("preis")] PriceMissing,
    [JsonStringEnumMemberName("rest")] Leftover,
    [JsonStringEnumMemberName("rezeptur")] Unused,
}

// Einsatz, dessen Umsatz über den Aufschlagsatz statt über Portion und Preis geschätzt wird.
// Basis ist die Sparte, deren Satz gilt; Unbestimmt steht für den Satz des Betriebs.
public sealed class EstimateRow
{
    public EstimateSource Source { get; set; }
    public string Name { get; set; } = "";
    public string Invoice { get; set; } = "";
    public DateOnly? Date { get; set; }
    public Unit Unit { get; set; }
    public long Qty { get; set; }
    public Sparte Basis { get; set; }
    public long Cost { get; set; }
    public long Markup { get; set; }
    public long RevenueNet { get; set; }
}

public sealed class ProductPortions
{
    public string ProductId { get; set; } = "";
    public long Portions { get; set; }
    public bool Pinned { get; set; }
}

public sealed class Leftover
{
    public string IngredientId { get; set; } = "";
    public long Qty { get; set; }
}

public sealed class Purchase
{
    public string InvoiceId { get; set; } = "";
    public string Invoice { get; set; } = "";
    public long LineNo { get; set; }
    public string Name { get; set; } = "";
    public long Quantity { get; set; }
    public string UnitCode { get; set; } = "";
    public Unit Unit { get; set; }
    public long Factor { get; set; }
    public long Per { get; set; } = 1;
    public FactorSource Source { get; set; }
    public long Qty { get; set; }
    public long Net { get; set; }
}

public sealed class IngredientProduct
{
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public long Portions { get; set; }
    public long PerPortion { get; set; }
    public long Qty => Portions * PerPortion;
}

public sealed class IngredientRow
{
    public string IngredientId { get; set; } = "";
    public string Name { get; set; } = "";
    public Unit Unit { get; set; }
    public List<Purchase> Purchases { get; set; } = [];
    public long Opening { get; set; }
    public long Closing { get; set; }
    public long Bought { get; set; }
    public long Cost { get; set; }
    public long Used { get; set; }
    public long UsedCost { get; set; }
    public YieldRule? Yield { get; set; }
    public long YieldRate { get; set; }
    public long Sellable { get; set; }
    public List<IngredientProduct> Products { get; set; } = [];
    public long Leftover { get; set; }
    public bool Binding { get; set; }
}

public sealed class Allocation
{
    public int Component { get; set; }
    public List<ProductPortions> Products { get; set; } = [];
    public List<string> Binding { get; set; } = [];
    public List<Leftover> Leftover { get; set; } = [];
    public long Grid { get; set; }
    public long States { get; set; }
    public bool Approximate { get; set; }
}

// Der Rohgewinnaufschlagsatz einer Sparte: der Einsatz der zugeteilten Portionen, ihr
// Nettoumsatz und der Satz, der das eine in das andere überführt.
public sealed class MarkupRow
{
    public Sparte Sparte { get; set; }
    public long Portions { get; set; }
    public long CostOfGoods { get; set; }
    public long RevenueNet { get; set; }
    public long GrossProfit => RevenueNet - CostOfGoods;
    public long Markup => Rohaufschlag.Of(RevenueNet, CostOfGoods);
}

public sealed class ProductRow
{
    public string ProductId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> Binding { get; set; } = [];
    public string PinReason { get; set; } = "";
    public Sparte Sparte { get; set; }
    public long Portions { get; set; }
    public long CostPerPortion { get; set; }
    public long CostOfGoods { get; set; }
    public bool Pinned { get; set; }
    public long GrossPrice { get; set; }
    public long Vat { get; set; }
    public long UnitNet { get; set; }
    public long RevenueNet { get; set; }
    public long Markup => Rohaufschlag.Of(RevenueNet, CostOfGoods);
    public bool PriceMissing { get; set; }
}

public sealed class Report
{
    public string CaseId { get; set; } = "";
    public DateTimeOffset ComputedAt { get; set; }
    public Totals Totals { get; set; } = new();
    public List<IngredientRow> Ingredients { get; set; } = [];
    public List<ProductRow> Products { get; set; } = [];
    public List<MarkupRow> Markups { get; set; } = [];
    public List<UnmappedLine> Unmapped { get; set; } = [];
    public List<UnusedLine> Unused { get; set; } = [];
    public List<UnusedLine> Deposits { get; set; } = [];
    public List<UnusedLine> NoRevenue { get; set; } = [];
    public List<EstimateRow> Estimated { get; set; } = [];
    public List<Allocation> Allocations { get; set; } = [];
    public List<Flag> Warnings { get; set; } = [];
}

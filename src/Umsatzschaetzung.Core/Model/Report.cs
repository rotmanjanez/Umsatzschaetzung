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
    public long GrossProfit => CalculatedRevenueNet - AllocatedCost;
    public long Markup => Rohaufschlag.Of(CalculatedRevenueNet, AllocatedCost);
    public long Portions { get; set; }
    public long UnmappedCost { get; set; }
    public long UnusedCost { get; set; }
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

public sealed class IngredientRow
{
    public string IngredientId { get; set; } = "";
    public string Name { get; set; } = "";
    public Unit Unit { get; set; }
    public List<Purchase> Purchases { get; set; } = [];
    public bool Estimated => Purchases.Exists(p => p.Source == FactorSource.Piece);
    public long Opening { get; set; }
    public long Closing { get; set; }
    public long Bought { get; set; }
    public long Cost { get; set; }
    public long Used { get; set; }
    public long UsedCost { get; set; }
    public YieldRule? Yield { get; set; }
    public bool YieldChosen { get; set; }
    public long YieldRate { get; set; }
    public long Sellable { get; set; }
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
    public List<Allocation> Allocations { get; set; } = [];
    public List<Flag> Warnings { get; set; } = [];
}

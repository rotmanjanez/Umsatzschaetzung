using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.Reports;

public sealed record PortionRow(string Name, string Portions, bool Pinned);

public sealed record LeftoverRow(string Name, string Qty);

public sealed record AllocationDisplay(int Component, List<PortionRow> Products, List<string> Binding, List<LeftoverRow> Leftover, string Grid, string States, bool Approximate);

public sealed record InvoiceRow(string Number, string Supplier, string Date, string Source, string NetTotal, string GrossTotal, int Lines, int Used, string UsedNet, string Verified);

public sealed record LineRow(string Invoice, string Date, long LineNo, string Name, string Quantity, string UnitPrice, string LineNet, string Vat);

public sealed record IngredientDisplay(string Name, string Opening, string Bought, string Closing, string Cost, string Used, string UsedCost, string Yield, string Sellable, string Leftover, bool Binding);

public sealed record YieldRateRow(string Ingredient, string Shrinkage, string OwnUse, string Staff, string Free, string Yield, string Source, bool Chosen);

public sealed record PinnedRow(string Product, string Portions, string Reason);

public sealed record FullDisplay(
    string Period,
    string Date,
    string ComputedAt,
    string RulesVersion,
    List<ProductRowDisplay> Products,
    List<IngredientDisplay> Ingredients,
    List<YieldRateRow> YieldRates,
    List<AllocationDisplay> Allocations,
    List<InvoiceRow> Invoices,
    List<LineRow> Lines,
    string PortionTotal,
    string IncludedNet,
    List<PinnedRow> Pinned,
    List<string> Warnings);

public static class Display
{
    public static ReportDisplay Report(Case c, Model.Report r, RuleSet rs) =>
        new(
            Summary(r),
            ProductRows(r, rs),
            Revenue(c, r),
            Tree(r.Root, c, rs),
            Excluded(c, r, rs));

    public static FullDisplay Full(Case c, Model.Report r, RuleSet rs)
    {
        var excluded = ExcludedLines(r);
        var invoices = new List<InvoiceRow>(c.Invoices.Count);
        foreach (var inv in c.Invoices)
        {
            var used = 0;
            long usedNet = 0;
            foreach (var l in inv.Lines)
            {
                if (excluded.Contains((inv.Id, l.No))) continue;
                used++;
                usedNet += l.LineNet;
            }
            invoices.Add(new InvoiceRow(inv.Number, inv.SupplierName, Format.Date(inv.Date), SourceName(inv.Source),
                Format.Cents(inv.NetTotal), Format.Cents(inv.GrossTotal), inv.Lines.Count, used, Format.Cents(usedNet), Verified(inv.Verification)));
        }
        return new FullDisplay(
            Period(c.PeriodFrom, c.PeriodTo),
            Format.Day(r.ComputedAt),
            Format.Timestamp(r.ComputedAt),
            Format.Group(rs.Version),
            ProductRows(r, rs),
            IngredientRows(c, r, rs),
            YieldRates(c, r, rs),
            r.Allocations.Select(a => Allocation(a, rs)).ToList(),
            invoices,
            Lines(c, r),
            Format.Group(r.Totals.Portions),
            Format.Cents(r.Totals.CostOfGoods + r.Totals.StockChange),
            c.Pinned.Select(p => new PinnedRow(ProductName(rs, p.ProductId), Format.Portions(p.Portions), p.Reason)).ToList(),
            r.Warnings.Select(w => w.Message).ToList());
    }

    public static List<KV> Summary(Model.Report r)
    {
        var s = r.Totals;
        return
        [
            new("calculatedRevenueNet", "Kalkulierter Umsatz (netto)", Format.Cents(s.CalculatedRevenueNet)),
            new("costOfGoods", "Wareneinsatz", Format.Cents(s.CostOfGoods)),
            new("grossProfit", "Rohgewinn", Format.Cents(s.GrossProfit)),
            new("markup", "Rohgewinnaufschlag", Format.Bp(s.Markup)),
            new("portions", "Portionen gesamt", Format.Portions(s.Portions)),
            new("purchases", "Erfasste Einkäufe (netto)", Format.Cents(s.Purchases)),
            new("stockChange", "Bestandsveränderung", Format.Cents(s.StockChange)),
            new("excluded", "Nicht berücksichtigt", $"{Format.Cents(s.UnmappedCost + s.UnusedCost)} ({Format.Bp(s.ExcludedShare)})"),
        ];
    }

    private static HashSet<(string, long)> ExcludedLines(Model.Report r)
    {
        var set = new HashSet<(string, long)>();
        foreach (var l in r.Unmapped) set.Add((l.InvoiceId, l.LineNo));
        foreach (var l in r.Unused) set.Add((l.InvoiceId, l.LineNo));
        return set;
    }

    public static ExcludedDisplay Excluded(Case c, Model.Report r, RuleSet rs)
    {
        var s = r.Totals;
        List<ExcludedRow> Unmapped() => r.Unmapped.Select(l => new ExcludedRow(
            InvoiceNumber(c, l.InvoiceId), l.LineNo, l.Name, "", Format.Cents(l.LineNet))).ToList();
        List<ExcludedRow> Unused() => r.Unused.Select(l => new ExcludedRow(
            InvoiceNumber(c, l.InvoiceId), l.LineNo, l.Name, IngredientName(rs, l.IngredientId),
            Format.Cents(l.LineNet))).ToList();
        return new ExcludedDisplay(
            Format.Cents(s.Purchases),
            Format.Cents(s.CostOfGoods + s.StockChange),
            Format.Cents(s.UnmappedCost + s.UnusedCost),
            Format.Bp(s.ExcludedShare),
            Unmapped(),
            Unused());
    }

    public static List<LineRow> Lines(Case c, Model.Report r)
    {
        var excluded = ExcludedLines(r);
        var rows = new List<LineRow>();
        foreach (var inv in c.Invoices.OrderBy(i => i.Date).ThenBy(i => i.Number, StringComparer.Ordinal))
        {
            foreach (var l in inv.Lines)
            {
                if (excluded.Contains((inv.Id, l.No))) continue;
                rows.Add(new LineRow(InvoiceNumber(c, inv.Id), Format.Date(inv.Date), l.No, l.Name,
                    LineQuantity(l), LineUnitPrice(l), Format.Cents(l.LineNet), Format.Bp(l.Vat)));
            }
        }
        return rows;
    }

    public static string LineQuantity(InvoiceLine l) => (Format.Milli(l.Quantity) + " " + Units.Label(l.UnitCode)).Trim();

    public static string LineUnitPrice(InvoiceLine l)
    {
        var p = Format.Micro(l.UnitPrice) + " €";
        if (l.PriceBaseQty > 0 && l.PriceBaseQty != 1000)
            p += " je " + Format.Milli(l.PriceBaseQty) + " " + Units.Label(l.UnitCode);
        return p;
    }

    private static readonly long[] VatRates = [1900, 700, 0];

    public static List<RevenueRow> Revenue(Case c, Model.Report r)
    {
        var declared = new Dictionary<long, long>();
        foreach (var d in c.Declared) declared[d.Vat] = declared.GetValueOrDefault(d.Vat) + d.Net;
        var calculated = new Dictionary<long, long>();
        foreach (var p in r.Products)
            if (!p.Disabled) calculated[p.Vat] = calculated.GetValueOrDefault(p.Vat) + p.RevenueNet;
        var rates = VatRates.Concat(calculated.Keys.Where(k => !VatRates.Contains(k)).Order()).ToList();
        var rows = new List<RevenueRow>(rates.Count + 1);
        long sumDeclared = 0, sumCalculated = 0;
        foreach (var rate in rates)
        {
            var d = declared.GetValueOrDefault(rate);
            var k = calculated.GetValueOrDefault(rate);
            if (d == 0 && k == 0) continue;
            sumDeclared += d;
            sumCalculated += k;
            rows.Add(new RevenueRow(Format.Bp(rate), Format.Cents(d), Format.Cents(k), Format.Cents(k - d), false));
        }
        rows.Add(new RevenueRow("Summe", Format.Cents(sumDeclared), Format.Cents(sumCalculated), Format.Cents(sumCalculated - sumDeclared), true));
        return rows;
    }

    public static List<ProductRowDisplay> ProductRows(Model.Report r, RuleSet rs) =>
        r.Products.Select(p => new ProductRowDisplay(
            p.ProductId,
            ProductName(rs, p.ProductId),
            RecipeOf(rs, p.ProductId),
            Format.Portions(p.Portions),
            Format.Group(p.Portions),
            p.Pinned,
            p.PriceMissing ? "" : Format.Cents(p.GrossPrice),
            Format.Bp(p.Vat),
            Format.Cents(p.RevenueNet),
            p.Disabled,
            p.PriceMissing)).ToList();

    public static List<IngredientDisplay> IngredientRows(Case c, Model.Report r, RuleSet rs)
    {
        var binding = r.Allocations.SelectMany(a => a.Binding).ToHashSet();
        var stock = new Dictionary<string, InventoryEntry>();
        foreach (var e in c.Inventory) stock[e.IngredientId] = e;
        var rows = new List<IngredientDisplay>(r.Ingredients.Count);
        foreach (var i in r.Ingredients)
        {
            var unit = IngredientUnit(rs, i.IngredientId);
            var e = stock.GetValueOrDefault(i.IngredientId) ?? new InventoryEntry();
            rows.Add(new IngredientDisplay(
                IngredientName(rs, i.IngredientId),
                Format.Qty(e.Opening, unit),
                Format.Qty(i.Bought, unit),
                Format.Qty(e.Closing, unit),
                Format.Cents(i.Cost),
                Format.Qty(i.Used, unit),
                Format.Cents(i.UsedCost),
                Format.Bp(YieldRate(c, rs, i.IngredientId)),
                Format.Qty(i.Sellable, unit),
                Format.Qty(i.Leftover, unit),
                binding.Contains(i.IngredientId)));
        }
        return rows;
    }

    private static long YieldRate(Case c, RuleSet rs, string id) =>
        rs.Ingredients.TryGetValue(id, out var ing) && Match.YieldRule(c, rs, ing) is var (rule, _) ? Yield(rule) : Bp.Full;

    private static long Yield(YieldRule r) => Bp.Full - r.Shrinkage - r.OwnUse - r.Staff - r.Free;

    public static List<YieldRateRow> YieldRates(Case c, Model.Report r, RuleSet rs)
    {
        var rows = new List<YieldRateRow>(r.Ingredients.Count);
        foreach (var i in r.Ingredients)
        {
            if (!rs.Ingredients.TryGetValue(i.IngredientId, out var ing) || Match.YieldRule(c, rs, ing) is not var (rule, chosen)) continue;
            rows.Add(new YieldRateRow(ing.Name, Format.Bp(rule.Shrinkage), Format.Bp(rule.OwnUse), Format.Bp(rule.Staff), Format.Bp(rule.Free),
                Format.Bp(Yield(rule)), rule.Source, chosen));
        }
        return rows;
    }

    public static string Period(DateOnly from, DateOnly to) => Format.Date(from) + " bis " + Format.Date(to);

    public static NodeDisplay Tree(Node n, Case c, RuleSet rs) => new(
        n.Label,
        Format.Value(n.Value, n.Unit),
        string.IsNullOrEmpty(n.Formula) ? null : n.Formula,
        n.Sources.Select(s => SourceText(s, c, rs)).ToList(),
        n.Inputs.Select(i => Tree(i, c, rs)).ToList());

    private static string SourceText(SourceRef s, Case c, RuleSet rs) => s.Kind switch
    {
        SourceKind.InvoiceLine => $"Rechnung {InvoiceNumber(c, s.InvoiceId ?? "")} Zeile {s.LineNo}",
        SourceKind.Rule => RuleText(s, rs),
        SourceKind.Pinned => WithReason("Fixierte Portionen", s.Reason),
        SourceKind.Allocation => WithReason("Zuteilung", s.Reason),
        SourceKind.Inventory => WithReason("Inventur", s.Reason),
        SourceKind.Case => WithReason("Angabe im Fall", s.Reason),
        _ => WithReason(s.Kind.ToString(), s.Reason),
    };

    private static string RuleText(SourceRef s, RuleSet rs)
    {
        var name = (s.Entity is { } kind ? rs.Find(kind, s.EntityId ?? "") : null) switch
        {
            ArticleMapping m => "Zuordnung " + IngredientName(rs, m.IngredientId),
            Ingredient i => i.Name,
            Product p => p.Name,
            YieldRule y => y.Name,
            _ => "",
        };
        return WithReason(name == "" ? "Regel" : "Regel „" + name + "“", s.Reason);
    }

    private static string WithReason(string text, string? reason) =>
        string.IsNullOrEmpty(reason) ? text : text + ": " + reason;

    private static AllocationDisplay Allocation(Allocation a, RuleSet rs) => new(
        a.Component,
        a.Products.Select(p => new PortionRow(ProductName(rs, p.ProductId), Format.Portions(p.Portions), p.Pinned)).ToList(),
        a.Binding.Select(id => IngredientName(rs, id)).ToList(),
        a.Leftover.Select(l => new LeftoverRow(IngredientName(rs, l.IngredientId), Format.Qty(l.Qty, IngredientUnit(rs, l.IngredientId)))).ToList(),
        Format.Group(a.Grid),
        Format.Group(a.States),
        a.Approximate);

    public static string SourceName(Source s) => s switch
    {
        Source.Ubl => "XRechnung (UBL)",
        Source.Cii => "XRechnung (CII)",
        Source.Zugferd => "ZUGFeRD",
        Source.Scan => "Scan",
        _ => s.ToString(),
    };

    public static string Verified(Verification? v) => v is null ? "" : "geprüft am " + Format.Timestamp(v.At);

    public static string InvoiceNumber(Case c, string id)
    {
        foreach (var inv in c.Invoices)
            if (inv.Id == id) return inv.Number != "" ? inv.Number : inv.FileName;
        return id;
    }

    public static string IngredientName(RuleSet rs, string id) =>
        rs.Ingredients.TryGetValue(id, out var e) ? e.Name : id;

    public static Unit IngredientUnit(RuleSet rs, string id) =>
        rs.Ingredients.TryGetValue(id, out var e) ? e.BaseUnit : Unit.Piece;

    public static string ProductName(RuleSet rs, string id) =>
        rs.Products.TryGetValue(id, out var e) ? e.Name : id;

    public static string RecipeOf(RuleSet rs, string id) =>
        rs.Products.TryGetValue(id, out var p) ? Recipe(rs, p) : "";

    public static string Recipe(RuleSet rs, Product p) =>
        string.Join(", ", p.Recipe.Select(l => Format.Qty(l.Amount, IngredientUnit(rs, l.IngredientId)) + " " + IngredientName(rs, l.IngredientId)));

    public static string ProductNote(ProductRowDisplay p) => p.Disabled ? "deaktiviert" : p.PriceMissing ? "Preis fehlt" : "";

    public static RuleSetDisplay Rules(RuleSet rs) =>
        new(rs.Products.ToDictionary(kv => kv.Key, kv => new ProductDisplay(Recipe(rs, kv.Value))),
            rs.Categories.ToDictionary(kv => kv.Key, kv => kv.Value.Name));

    public static CaseDisplay Case(Case c, RuleSet rs)
    {
        var declared = new Dictionary<long, string>();
        foreach (var d in c.Declared) declared[d.Vat] = Format.Cents(d.Net);
        return new CaseDisplay(
            Period(c.PeriodFrom, c.PeriodTo),
            Format.Date(c.PeriodFrom),
            Format.Date(c.PeriodTo),
            declared,
            c.Invoices.ToDictionary(i => i.Id, i => Invoice(i, rs)));
    }

    public static InvoiceDisplay Invoice(Model.Invoice inv, RuleSet rs) => new(
        Format.Date(inv.Date),
        Format.Cents(inv.NetTotal),
        Format.Cents(inv.GrossTotal),
        inv.Lines.Select(l => new LineDisplay(LineQuantity(l), LineUnitPrice(l), Format.Cents(l.LineNet), Format.Bp(l.Vat), MappingLabel(rs, l.MappingId))).ToList());

    public static string MappingLabel(RuleSet rs, string? id)
    {
        if (string.IsNullOrEmpty(id)) return "ungeklärt";
        return rs.Mappings.TryGetValue(id, out var m) ? CandidateLabel(rs, m) : "ungeklärt (Zuordnung " + id + " unbekannt)";
    }

    public static string CandidateLabel(RuleSet rs, ArticleMapping m) =>
        IngredientName(rs, m.IngredientId) + " × " + Format.Qty(m.Factor, IngredientUnit(rs, m.IngredientId));
}

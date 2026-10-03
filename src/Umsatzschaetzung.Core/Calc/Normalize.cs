using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Calc;

internal sealed record Excluded(List<UnmappedLine> Unmapped, List<UnusedLine> Unused, List<UnusedLine> Deposits, List<UnusedLine> NoRevenue);

public static class Normalize
{
    public const string Deposit = "prod.pfand";

    // Ein Einkauf zählt, wo sein Produkt verkauft wird oder in einem verkauften steckt.
    internal static (SortedDictionary<string, Stock> Uses, Excluded Ex, List<Flag> Flags) Run(Case c, RuleSet rs)
    {
        var uses = new SortedDictionary<string, Stock>(StringComparer.Ordinal);
        var reachable = Scale.Reached(c, rs);
        var ex = new Excluded([], [], [], []);
        var noRevenue = c.NoRevenue.ToHashSet(StringComparer.Ordinal);
        List<Flag> flags = [];
        var estimated = new SortedDictionary<string, int>(StringComparer.Ordinal);
        HashSet<string> stale = [];
        Stock Use(string id)
        {
            if (!uses.TryGetValue(id, out var u))
            {
                u = new Stock { ProductId = id };
                uses[id] = u;
            }
            return u;
        }
        foreach (var inv in c.Invoices)
            foreach (var line in inv.Lines)
            {
                var m = Match.Mapping(rs, inv.SupplierName, inv.Date, line);
                if (m is null || !rs.Products.TryGetValue(m.ProductId, out var product))
                {
                    ex.Unmapped.Add(new UnmappedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet });
                    continue;
                }
                if (product.Id == Deposit)
                {
                    ex.Deposits.Add(new UnusedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet, ProductId = product.Id });
                    continue;
                }
                if (noRevenue.Contains(product.Id))
                {
                    ex.NoRevenue.Add(new UnusedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet, ProductId = product.Id });
                    continue;
                }
                if (!reachable.Contains(product.Id) || Scale.Counted(rs, product.Id, reachable) is not { } unit)
                {
                    ex.Unused.Add(new UnusedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet, ProductId = product.Id });
                    continue;
                }
                if (Convert(line, m, unit, product.Piece) is not { } conv)
                {
                    ex.Unused.Add(new UnusedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet, ProductId = product.Id });
                    flags.Add(new Flag
                    {
                        Code = "missing_factor",
                        LineNo = line.No,
                        Message = $"Rechnung {inv.Number} Pos. {line.No} „{line.Name}“: {Units.Label(line.UnitCode)} lässt sich nicht "
                            + $"in {Format.UnitName(unit)} umrechnen — der Zuordnung fehlt der Faktor, der Umsatz ist über den Aufschlagsatz geschätzt",
                    });
                    continue;
                }
                if (conv.Source == FactorSource.Table && m.Factor is not null && c.Mappings.GetValueOrDefault(m.Id) == m && stale.Add(m.Id))
                    flags.Add(new Flag
                    {
                        Code = "mapping_unit",
                        LineNo = line.No,
                        Message = $"Zuordnung „{line.Name}“: ihr Faktor gilt für eine andere Einheit, „{product.Name}“ zählt in {Format.UnitName(unit)} — "
                            + $"gerechnet ist {Units.Label(line.UnitCode)} ohne Faktor, bitte die Zuordnung prüfen",
                    });
                var u = Use(product.Id);
                u.Bought += conv.Qty;
                u.Cost += line.LineNet;
                u.Purchases.Add(new Purchase
                {
                    InvoiceId = inv.Id,
                    Invoice = Names.Invoice(inv),
                    LineNo = line.No,
                    Name = line.Name,
                    Quantity = line.Quantity,
                    UnitCode = line.UnitCode,
                    Unit = unit,
                    Factor = conv.Factor,
                    Per = conv.Per,
                    Source = conv.Source,
                    Qty = conv.Qty,
                    Net = line.LineNet,
                });
                if (conv.Source == FactorSource.Piece) estimated[product.Id] = estimated.GetValueOrDefault(product.Id) + 1;
            }
        foreach (var (id, n) in estimated)
        {
            var p = rs.Products[id];
            flags.Add(new Flag
            {
                Code = "piece_weight",
                Message = $"„{p.Name}“: {n} {(n == 1 ? "Position" : "Positionen")} über das Stückgewicht umgerechnet — "
                    + $"1 Stk {p.Name} ≈ {Format.Qty(p.Piece!.Amount, p.Piece.Unit)} (Richtwert des Produkts)",
            });
        }
        foreach (var u in uses.Values) u.Used = u.Bought;
        foreach (var e in c.Inventory)
        {
            if (!rs.Products.TryGetValue(e.ProductId, out var p) || !reachable.Contains(e.ProductId) || noRevenue.Contains(e.ProductId)) continue;
            if (Scale.Of(p) is not { } counted) continue;
            if (Units.Lookup(e.Unit)?.Base != counted)
            {
                flags.Add(new Flag
                {
                    Code = "stock_unit",
                    Message = $"Bestand „{p.Name}“ steht in {Units.Label(e.Unit)}, das Produkt zählt in {Format.UnitName(counted)} — nicht berücksichtigt",
                });
                continue;
            }
            var u = Use(e.ProductId);
            u.Opening = Scale.ToBase(e.Opening, e.Unit);
            u.Closing = Scale.ToBase(e.Closing, e.Unit);
            u.Used = u.Opening + u.Bought - u.Closing;
        }
        var enabled = Calculation.EnabledProducts(c, rs).ToHashSet(StringComparer.Ordinal);
        foreach (var cp in c.Products.Where(p => enabled.Contains(p.ProductId) && !noRevenue.Contains(p.ProductId)))
            foreach (var l in cp.Recipe ?? [])
                if (!Recipes.Fits(rs, l) && rs.Products.TryGetValue(l.PartId, out var part) && Scale.Of(part) is { } counted)
                    flags.Add(new Flag
                    {
                        Code = "recipe_unit",
                        Message = $"Rezeptur „{Names.Product(rs, cp.ProductId)}“: „{part.Name}“ steht in {Units.Label(l.Unit)}, das Produkt zählt in {Format.UnitName(counted)} — Zeile nicht berücksichtigt",
                    });
        foreach (var u in uses.Values)
        {
            u.UsedCost = u.Cost;
            if (u.Bought > 0) u.UsedCost = u.Cost * u.Used / u.Bought;
        }
        return (uses, ex, flags);
    }

    // Eine Rechnungsposition in der Einheit des Produkts. 2 kg sind 2.000 g, ohne dass jemand etwas
    // pflegen muss; Gebinde und Dimensionswechsel rechnet der Faktor, den Factors findet.
    static (long Qty, long Factor, long Per, FactorSource Source)? Convert(InvoiceLine line, ArticleMapping m, Unit unit, Piece? piece) =>
        Factors.Of(unit, piece, line.UnitCode, PackSize.Read(line.Name), m.Factor) is { } f
            ? (Factors.Qty(line.Quantity, line.UnitCode, f), f.Factor, f.Per, f.Source)
            : null;
}

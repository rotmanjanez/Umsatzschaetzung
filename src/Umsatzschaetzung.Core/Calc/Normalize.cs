using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Calc;

internal sealed record Excluded(List<UnmappedLine> Unmapped, List<UnusedLine> Unused);

public static class Normalize
{
    static HashSet<string> RecipeIngredients(Case c, RuleSet rs)
    {
        HashSet<string> output = [];
        foreach (var id in Calculation.EnabledProducts(c, rs))
            foreach (var r in rs.Products[id].Recipe)
                output.Add(r.IngredientId);
        return output;
    }

    internal static (SortedDictionary<string, IngredientUse> Uses, Excluded Ex, List<Flag> Flags) Run(Case c, RuleSet rs)
    {
        var uses = new SortedDictionary<string, IngredientUse>(StringComparer.Ordinal);
        var inRecipe = RecipeIngredients(c, rs);
        var ex = new Excluded([], []);
        List<Flag> flags = [];
        var estimated = new SortedDictionary<string, int>(StringComparer.Ordinal);
        IngredientUse Use(string id)
        {
            if (!uses.TryGetValue(id, out var u))
            {
                u = new IngredientUse { IngredientId = id };
                uses[id] = u;
            }
            return u;
        }
        foreach (var inv in c.Invoices)
            foreach (var line in inv.Lines)
            {
                var m = Match.Mapping(rs, inv.SupplierName, inv.Date, line);
                if (m is null || !rs.Ingredients.TryGetValue(m.IngredientId, out var ing))
                {
                    ex.Unmapped.Add(new UnmappedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet });
                    continue;
                }
                if (!inRecipe.Contains(ing.Id) || Scale.Of(rs, ing.Id) is not { } unit)
                {
                    ex.Unused.Add(new UnusedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet, IngredientId = ing.Id });
                    continue;
                }
                if (Convert(line, m, unit, ing.Piece) is not { } conv)
                {
                    ex.Unmapped.Add(new UnmappedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet });
                    flags.Add(new Flag
                    {
                        Code = "missing_factor",
                        LineNo = line.No,
                        Message = $"Rechnung {inv.Number} Pos. {line.No} „{line.Name}“: {Units.Label(line.UnitCode)} lässt sich nicht "
                            + $"in {Format.UnitName(unit)} umrechnen — der Zuordnung fehlt der Faktor",
                    });
                    continue;
                }
                var u = Use(ing.Id);
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
                if (conv.Source == FactorSource.Piece) estimated[ing.Id] = estimated.GetValueOrDefault(ing.Id) + 1;
            }
        foreach (var (id, n) in estimated)
        {
            var ing = rs.Ingredients[id];
            flags.Add(new Flag
            {
                Code = "piece_weight",
                Message = $"„{ing.Name}“: {n} {(n == 1 ? "Position" : "Positionen")} über das Stückgewicht umgerechnet — "
                    + $"1 Stk {ing.Name} ≈ {Format.Qty(ing.Piece!.Amount, ing.Piece.Unit)} (Richtwert der Zutat)",
            });
        }
        foreach (var u in uses.Values) u.Used = u.Bought;
        foreach (var e in c.Inventory)
        {
            if (!rs.Ingredients.ContainsKey(e.IngredientId) || !inRecipe.Contains(e.IngredientId)) continue;
            if (Scale.Of(rs, e.IngredientId) is null) continue;
            var u = Use(e.IngredientId);
            u.Opening = Scale.ToBase(e.Opening, e.Unit);
            u.Closing = Scale.ToBase(e.Closing, e.Unit);
            u.Used = u.Opening + u.Bought - u.Closing;
        }
        foreach (var u in uses.Values)
        {
            u.UsedCost = u.Cost;
            if (u.Bought > 0) u.UsedCost = u.Cost * u.Used / u.Bought;
        }
        return (uses, ex, flags);
    }

    // Eine Rechnungsposition in der Rezepteinheit. 2 kg sind 2.000 g, ohne dass jemand etwas
    // pflegen muss; Gebinde und Dimensionswechsel rechnet der Faktor, den Factors findet.
    static (long Qty, long Factor, long Per, FactorSource Source)? Convert(InvoiceLine line, ArticleMapping m, Unit unit, Piece? piece) =>
        Factors.Of(unit, piece, line.UnitCode, PackSize.Read(line.Name), m.Factor) is { } f
            ? (Factors.Qty(line.Quantity, line.UnitCode, f), f.Factor, f.Per, f.Source)
            : null;
}

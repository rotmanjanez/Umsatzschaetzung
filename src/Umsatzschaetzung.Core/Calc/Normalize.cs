using Umsatzschaetzung.Model;

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
                if (Convert(line, m, unit) is not { } conv)
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
                if (!m.Confirmed)
                    flags.Add(new Flag
                    {
                        Code = "unconfirmed_mapping",
                        LineNo = line.No,
                        Message = $"Rechnung {inv.Number} Pos. {line.No} „{line.Name}“: Zuordnung zu „{ing.Name}“ ist ein unbestätigter automatischer Vorschlag",
                    });
                var u = Use(ing.Id);
                u.Bought += conv.Qty;
                u.Cost += line.LineNet;
                var node = new Node
                {
                    Label = $"Rechnung {inv.Number} Pos. {line.No}: {line.Name}",
                    Value = conv.Qty,
                    Unit = Units.Value(unit),
                    Formula = $"{conv.Step} = {Format.Qty(conv.Qty, unit)} (netto {Format.Cents(line.LineNet)})",
                    Sources =
                    [
                        new SourceRef { Kind = SourceKind.InvoiceLine, InvoiceId = inv.Id, LineNo = line.No },
                        new SourceRef { Kind = SourceKind.Rule, Entity = Entity.Mapping, EntityId = m.Id },
                    ],
                };
                u.Node ??= new Node { Label = ing.Name + ": Einkauf", Unit = Units.Value(unit) };
                u.Node.Inputs.Add(node);
            }
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            var unit = Scale.Of(rs, id) ?? Unit.Piece;
            u.Node!.Value = u.Bought;
            u.Node.Formula = $"Summe aus {u.Node.Inputs.Count} Rechnungspositionen = {Format.Qty(u.Bought, unit)} (netto {Format.Cents(u.Cost)})";
            u.Used = u.Bought;
        }
        foreach (var e in c.Inventory)
        {
            if (!rs.Ingredients.TryGetValue(e.IngredientId, out var ing) || !inRecipe.Contains(e.IngredientId)) continue;
            if (Scale.Of(rs, e.IngredientId) is not { } unit) continue;
            var opening = Scale.ToBase(e.Opening, e.Unit);
            var closing = Scale.ToBase(e.Closing, e.Unit);
            var u = Use(e.IngredientId);
            u.Node ??= new Node
            {
                Label = ing.Name + ": Einkauf",
                Unit = Units.Value(unit),
                Formula = "keine Rechnungsposition = " + Format.Qty(0, unit),
            };
            u.Used = opening + u.Bought - closing;
            u.Node = new Node
            {
                Label = ing.Name + ": Verbrauch",
                Value = u.Used,
                Unit = Units.Value(unit),
                Formula = $"Anfangsbestand {Format.Qty(opening, unit)} + Einkauf {Format.Qty(u.Bought, unit)} − Endbestand {Format.Qty(closing, unit)} = {Format.Qty(u.Used, unit)}",
                Inputs = [u.Node],
                Sources = [new SourceRef { Kind = SourceKind.Inventory, Entity = Entity.Ingredient, EntityId = e.IngredientId }],
            };
        }
        foreach (var u in uses.Values)
        {
            u.UsedCost = u.Cost;
            if (u.Bought > 0) u.UsedCost = u.Cost * u.Used / u.Bought;
        }
        return (uses, ex, flags);
    }

    // Eine Rechnungsposition in der Rezepteinheit. Bei Gebinden und über Dimensionsgrenzen
    // hinweg sagt nur der Faktor der Zuordnung, wie viel drin ist; sonst rechnet die
    // Einheitentabelle -- 2 kg sind 2.000 g, ohne dass jemand etwas pflegen muss.
    static (long Qty, string Step)? Convert(InvoiceLine line, ArticleMapping m, Unit unit)
    {
        var billed = Units.Lookup(line.UnitCode);
        var qty = Format.Milli(line.Quantity);
        if (billed is { Container: false } u && u.Base == unit)
            return (line.Quantity * u.Factor / 1000, $"{qty} {u.Name}");
        if (m.Factor is not { } factor) return null;
        var label = billed?.Name ?? line.UnitCode;
        return (line.Quantity * factor / 1000, $"{qty} {label} × {Format.Qty(factor, unit)}");
    }
}

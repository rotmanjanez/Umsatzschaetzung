using Ausbeute.Model;

namespace Ausbeute.Calc;

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
                var m = Match.Mapping(rs, inv.SupplierVatId, inv.Date, line);
                if (m is null || !rs.Ingredients.TryGetValue(m.IngredientId, out var ing))
                {
                    ex.Unmapped.Add(new UnmappedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet });
                    continue;
                }
                if (!inRecipe.Contains(ing.Id))
                {
                    ex.Unused.Add(new UnusedLine { InvoiceId = inv.Id, LineNo = line.No, Name = line.Name, LineNet = line.LineNet, IngredientId = ing.Id });
                    continue;
                }
                if (!m.Confirmed)
                    flags.Add(new Flag
                    {
                        Code = "unconfirmed_mapping",
                        LineNo = line.No,
                        Message = $"Rechnung {inv.Number} Pos. {line.No} „{line.Name}“: Zuordnung zu „{ing.Name}“ ist ein unbestätigter automatischer Vorschlag",
                    });
                var q = line.Quantity * m.Factor / 1000;
                var u = Use(ing.Id);
                u.Bought += q;
                u.Cost += line.LineNet;
                var node = new Node
                {
                    Label = $"Rechnung {inv.Number} Pos. {line.No}: {line.Name}",
                    Value = q,
                    Unit = Units.Value(ing.BaseUnit),
                    Formula = $"{Format.Milli(line.Quantity)} × {Format.Qty(m.Factor, ing.BaseUnit)} = {Format.Qty(q, ing.BaseUnit)} (netto {Format.Cents(line.LineNet)})",
                    Sources =
                    [
                        new SourceRef { Kind = SourceKind.InvoiceLine, InvoiceId = inv.Id, LineNo = line.No },
                        new SourceRef { Kind = SourceKind.Rule, Entity = Entity.Mapping, EntityId = m.Id, ChangeId = m.Meta.ChangeId },
                    ],
                };
                u.Node ??= new Node { Label = ing.Name + ": Einkauf", Unit = Units.Value(ing.BaseUnit) };
                u.Node.Inputs.Add(node);
            }
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            var ing = rs.Ingredients[id];
            u.Node!.Value = u.Bought;
            u.Node.Formula = $"Summe aus {u.Node.Inputs.Count} Rechnungspositionen = {Format.Qty(u.Bought, ing.BaseUnit)} (netto {Format.Cents(u.Cost)})";
            u.Used = u.Bought;
        }
        foreach (var e in c.Inventory)
        {
            if (!rs.Ingredients.TryGetValue(e.IngredientId, out var ing) || !inRecipe.Contains(e.IngredientId)) continue;
            var u = Use(e.IngredientId);
            u.Node ??= new Node
            {
                Label = ing.Name + ": Einkauf",
                Unit = Units.Value(ing.BaseUnit),
                Formula = "keine Rechnungsposition = " + Format.Qty(0, ing.BaseUnit),
            };
            u.Used = e.Opening + u.Bought - e.Closing;
            u.Node = new Node
            {
                Label = ing.Name + ": Verbrauch",
                Value = u.Used,
                Unit = Units.Value(ing.BaseUnit),
                Formula = $"Anfangsbestand {Format.Qty(e.Opening, ing.BaseUnit)} + Einkauf {Format.Qty(u.Bought, ing.BaseUnit)} − Endbestand {Format.Qty(e.Closing, ing.BaseUnit)} = {Format.Qty(u.Used, ing.BaseUnit)}",
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
}

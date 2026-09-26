namespace Umsatzschaetzung.Model;

// Die Rezeptur legt fest, worin eine Zutat gemessen wird. Die Zutat selbst trägt
// keine Einheit: "Pommes in Stück" und "Pommes in Gramm" sind nicht dieselbe Zutat.
public static class Scale
{
    public static Unit? Of(RuleSet rs, string ingredientId) => Bases(rs).GetValueOrDefault(ingredientId);

    public static Dictionary<string, Unit?> Bases(RuleSet rs) => rs.Bases ??= Compute(rs);

    // Eine unbekannte Einheit oder zwei verschiedene Basen lassen die Zutat ohne Basis.
    static Dictionary<string, Unit?> Compute(RuleSet rs)
    {
        var output = new Dictionary<string, Unit?>(StringComparer.Ordinal);
        foreach (var p in rs.Products.Values)
            foreach (var line in p.Recipe)
            {
                if (line.ProductId is not null) continue;
                var u = Units.Lookup(line.Unit)?.Base;
                if (!output.TryGetValue(line.IngredientId, out var seen)) output[line.IngredientId] = u;
                else if (seen != u) output[line.IngredientId] = null;
            }
        return output;
    }

    public static List<Flag> Conflicts(RuleSet rs, IEnumerable<string> ingredientIds)
    {
        var wanted = ingredientIds.ToHashSet(StringComparer.Ordinal);
        var seen = new Dictionary<string, (Unit Base, string Product)>(StringComparer.Ordinal);
        List<Flag> flags = [];
        foreach (var id in rs.Products.Keys.Order(StringComparer.Ordinal))
            foreach (var line in rs.Products[id].Recipe)
            {
                if (line.ProductId is not null || !wanted.Contains(line.IngredientId)) continue;
                var name = rs.Ingredients.TryGetValue(line.IngredientId, out var ing) ? ing.Name : line.IngredientId;
                if (Units.Lookup(line.Unit) is not { } u)
                {
                    flags.Add(new Flag
                    {
                        Code = "unknown_recipe_unit",
                        Message = $"Rezeptur „{rs.Products[id].Name}“: „{name}“ hat die unbekannte Einheit „{line.Unit}“",
                    });
                    continue;
                }
                if (!seen.TryGetValue(line.IngredientId, out var first)) seen[line.IngredientId] = (u.Base, rs.Products[id].Name);
                else if (first.Base != u.Base)
                    flags.Add(new Flag
                    {
                        Code = "recipe_unit_conflict",
                        Message = $"„{name}“ wird in „{first.Product}“ in {Format.UnitName(first.Base)} und in "
                            + $"„{rs.Products[id].Name}“ in {Format.UnitName(u.Base)} gerechnet",
                    });
            }
        return flags;
    }

    public static bool NeedsFactor(RuleSet rs, string ingredientId, InvoiceLine line) =>
        Of(rs, ingredientId) is not null && Factors.Of(rs, ingredientId, line, null) is null;

    public static long ToBase(long amount, string unit) =>
        Units.Lookup(unit) is { } u ? amount * u.Factor : amount;
}

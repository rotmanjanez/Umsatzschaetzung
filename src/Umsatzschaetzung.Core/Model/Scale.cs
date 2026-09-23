namespace Umsatzschaetzung.Model;

// Die Rezeptur legt fest, worin eine Zutat gemessen wird. Die Zutat selbst trägt
// keine Einheit: "Pommes in Stück" und "Pommes in Gramm" sind nicht dieselbe Zutat.
public static class Scale
{
    public static Unit? Of(RuleSet rs, string ingredientId)
    {
        Unit? found = null;
        foreach (var id in rs.Products.Keys.Order(StringComparer.Ordinal))
            foreach (var line in rs.Products[id].Recipe)
            {
                if (line.IngredientId != ingredientId) continue;
                if (Units.Lookup(line.Unit) is not { } u) return null;
                if (found is { } b && b != u.Base) return null;
                found = u.Base;
            }
        return found;
    }

    public static List<Flag> Conflicts(RuleSet rs, IEnumerable<string> ingredientIds)
    {
        var wanted = ingredientIds.ToHashSet(StringComparer.Ordinal);
        var seen = new Dictionary<string, (Unit Base, string Product)>(StringComparer.Ordinal);
        List<Flag> flags = [];
        foreach (var id in rs.Products.Keys.Order(StringComparer.Ordinal))
            foreach (var line in rs.Products[id].Recipe)
            {
                if (!wanted.Contains(line.IngredientId)) continue;
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

    public static bool NeedsFactor(RuleSet rs, string ingredientId, string unitCode) =>
        Of(rs, ingredientId) is { } unit && !(Units.Lookup(unitCode) is { Container: false } u && u.Base == unit);

    public static long ToBase(long amount, string unit) =>
        Units.Lookup(unit) is { } u ? amount * u.Factor : amount;
}

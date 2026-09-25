using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

internal static class Yield
{
    public static void Run(Case c, RuleSet rs, SortedDictionary<string, IngredientUse> uses)
    {
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            if (!rs.Ingredients.TryGetValue(id, out var ing))
                throw new InvalidOperationException($"yield: unknown ingredient \"{id}\"");
            u.Yield = Match.YieldRule(c, rs, ing);
            u.YieldRate = Bp.Full - (u.Yield?.Deduction ?? 0);
            u.Sellable = u.Used * u.YieldRate / Bp.Full;
        }
    }
}

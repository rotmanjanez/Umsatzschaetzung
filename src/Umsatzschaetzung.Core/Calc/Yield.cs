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
            if (Match.YieldRule(c, rs, ing) is var (rule, _))
                u.Yield = rule;
            u.YieldRate = u.Yield is { } y ? Bp.Full - y.Shrinkage - y.OwnUse - y.Staff - y.Free : Bp.Full;
            u.Sellable = u.Used * u.YieldRate / Bp.Full;
        }
    }
}

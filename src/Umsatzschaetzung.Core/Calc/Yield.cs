using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

internal static class Yield
{
    public static void Run(Case c, RuleSet rs, SortedDictionary<string, Stock> uses)
    {
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            if (!rs.Products.TryGetValue(id, out var p))
                throw new InvalidOperationException($"yield: unknown product \"{id}\"");
            u.Yield = Match.YieldRule(c, rs, p);
            u.YieldRate = Bp.Full - (u.Yield?.Deduction ?? 0);
            u.Sellable = u.Used * u.YieldRate / Bp.Full;
        }
    }
}

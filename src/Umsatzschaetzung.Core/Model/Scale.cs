namespace Umsatzschaetzung.Model;

public static class Scale
{
    public static Unit? Of(RuleSet rs, string productId) =>
        rs.Products.TryGetValue(productId, out var p) ? Of(p) : null;

    public static Unit? Of(Product p) => Units.Lookup(p.Unit) is { Container: false } u ? u.Base : null;

    public static bool NeedsFactor(RuleSet rs, string productId, InvoiceLine line, IReadOnlySet<string> reached) =>
        Counted(rs, productId, reached) is not null && Factors.Of(rs, productId, line, null) is null;

    // What a category in no Sparte holds, as cleaning, packaging or freight, is no Wareneinsatz and
    // counted in nothing, unless something sold reaches it: the cup of a "Kaffee to go" is counted.
    public static Unit? Counted(RuleSet rs, string productId, IReadOnlySet<string> reached) =>
        rs.Products.TryGetValue(productId, out var p) && (Goods(rs, p) || reached.Contains(p.Id)) ? Of(p) : null;

    public static bool Goods(RuleSet rs, Product p) =>
        string.IsNullOrEmpty(p.CategoryId) || rs.Categories.GetValueOrDefault(p.CategoryId)?.Sparte != Sparte.Unbestimmt;

    // What the case sells and everything its recipes are made of, in the rules Recipes.Effective gives.
    public static HashSet<string> Reached(Case c, RuleSet effective) =>
        Recipes.Reachable(effective, c.Products
            .Where(cp => effective.Products.TryGetValue(cp.ProductId, out var p) && p.Meta.ValidOn(c.PeriodTo))
            .Select(cp => cp.ProductId));

    // Without a case: everything a product of the rules is made of.
    public static HashSet<string> Parts(RuleSet rs) =>
        rs.Products.Values.SelectMany(p => p.Recipe).Select(l => l.PartId).ToHashSet(StringComparer.Ordinal);

    public static string Code(Unit u) => u switch
    {
        Unit.G => "GRM",
        Unit.Ml => "MLT",
        _ => "H87",
    };

    public static long ToBase(long amount, string unit) =>
        Units.Lookup(unit) is { } u ? amount * u.Factor : amount;
}

namespace Umsatzschaetzung.Model;

// Die Rezeptur der Prüfung geht der des Katalogs vor. Aufgelöst wird einmal am Eingang
// der Kalkulation; alles dahinter liest weiter rs.Products.
public static class Recipes
{
    public static RuleSet Effective(Case c, RuleSet rs)
    {
        Dictionary<string, Product>? products = null;
        foreach (var cp in c.Products)
        {
            if (cp.Recipe is null || !rs.Products.TryGetValue(cp.ProductId, out var p)) continue;
            products ??= new Dictionary<string, Product>(rs.Products);
            products[cp.ProductId] = new Product { Id = p.Id, Name = p.Name, Recipe = cp.Recipe, Meta = p.Meta };
        }
        if (products is null) return rs;
        return new RuleSet
        {
            Version = rs.Version,
            Categories = rs.Categories,
            Ingredients = rs.Ingredients,
            Mappings = rs.Mappings,
            Products = products,
            YieldRules = rs.YieldRules,
        };
    }

    public static bool Adjusted(Case c, string productId) =>
        c.Products.Find(p => p.ProductId == productId)?.Recipe is not null;

    public static bool Stale(CaseProduct cp, RuleSet rs) =>
        cp.Recipe is not null && rs.Products.TryGetValue(cp.ProductId, out var p) && p.Meta.Rev != cp.RecipeBasis;

    public static bool Same(List<RecipeLine> a, List<RecipeLine> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].IngredientId != b[i].IngredientId || a[i].Amount != b[i].Amount || a[i].Unit != b[i].Unit) return false;
        return true;
    }
}

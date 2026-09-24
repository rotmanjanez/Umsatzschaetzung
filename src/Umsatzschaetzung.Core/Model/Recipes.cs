namespace Umsatzschaetzung.Model;

// Die Rezeptur der Prüfung geht der des Katalogs vor, Teilrezepte werden zu Zutaten
// aufgelöst. Beides geschieht einmal am Eingang der Kalkulation; alles dahinter liest
// flache Rezepte aus rs.Products.
public static class Recipes
{
    public static RuleSet Effective(Case c, RuleSet rs)
    {
        Dictionary<string, Product>? products = null;
        foreach (var (id, p) in rs.Products)
        {
            var recipe = c.Products.Find(cp => cp.ProductId == id)?.Recipe ?? Flat(rs, p);
            if (ReferenceEquals(recipe, p.Recipe)) continue;
            products ??= new Dictionary<string, Product>(rs.Products);
            products[id] = new Product { Id = p.Id, Name = p.Name, Recipe = recipe, Meta = p.Meta };
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

    // Die Zutaten einer Portion samt derer ihrer Teilrezepte; das Rezept selbst, wenn es keine hat.
    public static List<RecipeLine> Flat(RuleSet rs, Product p)
    {
        if (!p.Recipe.Exists(l => l.ProductId is not null)) return p.Recipe;
        List<RecipeLine> output = [];
        Expand(rs, p, 1, output, []);
        return output;
    }

    static void Expand(RuleSet rs, Product p, long portions, List<RecipeLine> output, HashSet<string> open)
    {
        if (!open.Add(p.Id)) throw new InvalidOperationException($"Rezept „{p.Name}“ enthält sich selbst");
        foreach (var l in p.Recipe)
        {
            if (l.ProductId is { } pid)
            {
                if (rs.Products.TryGetValue(pid, out var part)) Expand(rs, part, portions * l.Amount, output, open);
                continue;
            }
            var amount = portions * l.Amount;
            if (output.Find(o => o.IngredientId == l.IngredientId && o.Unit == l.Unit) is { } same) same.Amount += amount;
            else output.Add(new RecipeLine { IngredientId = l.IngredientId, Amount = amount, Unit = l.Unit });
        }
        open.Remove(p.Id);
    }

    public static bool Adjusted(Case c, string productId) =>
        c.Products.Find(p => p.ProductId == productId)?.Recipe is not null;

    public static bool Stale(CaseProduct cp, RuleSet rs) =>
        cp.Recipe is not null && rs.Products.TryGetValue(cp.ProductId, out var p) && p.Meta.Rev != cp.RecipeBasis;

    public static bool Same(List<RecipeLine> a, List<RecipeLine> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].IngredientId != b[i].IngredientId || a[i].ProductId != b[i].ProductId || a[i].Amount != b[i].Amount || a[i].Unit != b[i].Unit)
                return false;
        return true;
    }
}

namespace Umsatzschaetzung.Model;

public static class Names
{
    public static string Ingredient(RuleSet rs, string id) =>
        rs.Ingredients.TryGetValue(id, out var e) ? e.Name : id;

    public static Unit IngredientUnit(RuleSet rs, string id) => Scale.Of(rs, id) ?? Unit.Piece;

    public static string Product(RuleSet rs, string id) =>
        rs.Products.TryGetValue(id, out var e) ? e.Name : id;

    public static string Invoice(Invoice inv) => inv.Number != "" ? inv.Number : inv.FileName;

    public static string Invoice(Case c, string id)
    {
        foreach (var inv in c.Invoices)
            if (inv.Id == id) return Invoice(inv);
        return id;
    }

    public static string Recipe(RuleSet rs, Product p) =>
        string.Join(", ", p.Recipe.Select(l => RecipeLine(rs, l)));

    public static string RecipeLine(RuleSet rs, RecipeLine l) =>
        l.ProductId is { } part ? Format.Portions(l.Amount) + " " + Product(rs, part) : RecipeAmount(l) + " " + Ingredient(rs, l.IngredientId);

    public static string RecipeAmount(RecipeLine l) =>
        Units.Lookup(l.Unit) is { } u ? Format.Group(l.Amount) + " " + u.Name : Format.Group(l.Amount);

    public static string Mapping(RuleSet rs, string? id) =>
        string.IsNullOrEmpty(id) ? "ungeklärt"
            : rs.Mappings.TryGetValue(id, out var m) ? Candidate(rs, m)
            : "ungeklärt (Zuordnung " + id + " unbekannt)";

    public static string Candidate(RuleSet rs, ArticleMapping m) =>
        m.Factor is { } f
            ? Ingredient(rs, m.IngredientId) + " × " + Format.Qty(f, IngredientUnit(rs, m.IngredientId))
            : Ingredient(rs, m.IngredientId);
}

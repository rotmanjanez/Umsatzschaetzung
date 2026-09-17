using Umsatzschätzung.Model;

namespace Umsatzschätzung.Rules;

public sealed class RulesException(string message, Exception? inner = null) : Exception(message, inner);

public static class RuleCheck
{
    public static void Validate(RuleSet rs)
    {
        foreach (var (id, e) in rs.Categories) ValidateCategory(Keyed(id, e));
        foreach (var (id, e) in rs.Ingredients) ValidateIngredient(rs, Keyed(id, e));
        foreach (var (id, e) in rs.Mappings) ValidateMapping(rs, Keyed(id, e));
        foreach (var (id, e) in rs.Products) ValidateProduct(rs, Keyed(id, e));
        foreach (var (id, e) in rs.YieldRules) ValidateYieldRule(rs, Keyed(id, e));
    }

    // Names of the entries that would dangle if this entity were deleted.
    public static List<string> Users(RuleSet rs, Entity kind, string id) => kind switch
    {
        Entity.Category =>
        [
            .. Names(rs.Ingredients.Values.Where(i => i.CategoryId == id).Select(i => "Zutat \u201e" + i.Name + "\u201c")),
            .. Names(rs.YieldRules.Values.Where(y => y.CategoryId == id).Select(y => "Ausbeuteregel \u201e" + y.Name + "\u201c")),
        ],
        Entity.Ingredient =>
        [
            .. Names(rs.Mappings.Values.Where(m => m.IngredientId == id).Select(m => "Zuordnung \u201e" + (m.Name ?? m.SupplierArticleId ?? m.Gtin ?? m.Id) + "\u201c")),
            .. Names(rs.Products.Values.Where(p => p.Recipe.Exists(l => l.IngredientId == id)).Select(p => "Produkt \u201e" + p.Name + "\u201c")),
            .. Names(rs.YieldRules.Values.Where(y => y.IngredientId == id).Select(y => "Ausbeuteregel \u201e" + y.Name + "\u201c")),
        ],
        _ => [],
    };

    static IEnumerable<string> Names(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal);

    static T Keyed<T>(string id, T e) where T : IRuleEntity
    {
        if (id == "") throw new RulesException("leere Entitäts-ID");
        e.Id = id;
        e.Meta ??= new();
        if (e.Meta is { ValidFrom: { } from, ValidTo: { } to } && to <= from)
            throw new RulesException($"\"{id}\": „gültig bis“ muss nach „gültig ab“ liegen");
        return e;
    }

    static void ValidateCategory(Category e)
    {
        if (e.Name == "") throw new RulesException("Kategorie: Name darf nicht leer sein");
    }

    static void ValidateIngredient(RuleSet rs, Ingredient e)
    {
        if (e.Name == "") throw new RulesException("Zutat: Name darf nicht leer sein");
        if (!Enum.IsDefined(e.BaseUnit))
            throw new RulesException($"Zutat \"{e.Name}\": Basiseinheit muss ml, g oder piece sein");
        if (e.CategoryId != "" && !rs.Categories.ContainsKey(e.CategoryId))
            throw new RulesException($"Zutat \"{e.Name}\": Kategorie \"{e.CategoryId}\" existiert nicht");
    }

    static void ValidateMapping(RuleSet rs, ArticleMapping e)
    {
        if (string.IsNullOrEmpty(e.SupplierArticleId) && string.IsNullOrEmpty(e.Gtin) && string.IsNullOrEmpty(e.Name))
            throw new RulesException("Zuordnung: Artikelnummer, GTIN oder Namensmuster erforderlich");
        if (e.Factor <= 0) throw new RulesException("Zuordnung: Faktor muss größer als 0 sein");
        if (!rs.Ingredients.ContainsKey(e.IngredientId))
            throw new RulesException($"Zuordnung: Zutat \"{e.IngredientId}\" existiert nicht");
    }

    static void ValidateProduct(RuleSet rs, Product e)
    {
        if (e.Name == "") throw new RulesException("Produkt: Name darf nicht leer sein");
        if (e.Recipe is not { Count: > 0 })
            throw new RulesException($"Produkt \"{e.Name}\": Rezept darf nicht leer sein");
        foreach (var l in e.Recipe)
        {
            if (!rs.Ingredients.ContainsKey(l.IngredientId))
                throw new RulesException($"Produkt \"{e.Name}\": Zutat \"{l.IngredientId}\" existiert nicht");
            if (l.Amount <= 0)
                throw new RulesException($"Produkt \"{e.Name}\": Menge der Zutat \"{l.IngredientId}\" muss größer als 0 sein");
        }
    }

    static void ValidateYieldRule(RuleSet rs, YieldRule e)
    {
        if (e.Name == "") throw new RulesException("Ausbeuteregel: Name darf nicht leer sein");
        var category = e.CategoryId ?? "";
        var ingredient = e.IngredientId ?? "";
        if (category == "" && ingredient == "")
            throw new RulesException("Ausbeuteregel: Kategorie oder Zutat erforderlich");
        if (e.Default && e.Meta.ValidTo is null)
        {
            foreach (var (id, o) in rs.YieldRules)
            {
                if (id == e.Id || !o.Default || o.Meta?.ValidTo is not null) continue;
                if (ingredient != "" && (o.IngredientId ?? "") == ingredient)
                    throw new RulesException($"Ausbeuteregel \"{e.Name}\": für die Zutat ist bereits \"{o.Name}\" Standard");
                if (ingredient == "" && string.IsNullOrEmpty(o.IngredientId) && (o.CategoryId ?? "") == category)
                    throw new RulesException($"Ausbeuteregel \"{e.Name}\": für die Kategorie ist bereits \"{o.Name}\" Standard");
            }
        }
        if (ingredient != "" && !rs.Ingredients.ContainsKey(ingredient))
            throw new RulesException($"Ausbeuteregel: Zutat \"{ingredient}\" existiert nicht");
        if (category != "" && !rs.Categories.ContainsKey(category))
            throw new RulesException($"Ausbeuteregel: Kategorie \"{category}\" existiert nicht");
        if (e.Shrinkage < 0 || e.OwnUse < 0 || e.Staff < 0 || e.Free < 0)
            throw new RulesException("Ausbeuteregel: Anteile dürfen nicht negativ sein");
        if (e.Shrinkage + e.OwnUse + e.Staff + e.Free > Bp.Full)
            throw new RulesException("Ausbeuteregel: Summe der Anteile darf 100 % nicht überschreiten");
    }
}

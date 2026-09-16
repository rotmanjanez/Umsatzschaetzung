using System.Text;
using System.Text.Json.Serialization;

namespace Umsatzschätzung.Model;

public enum Entity
{
    [JsonStringEnumMemberName("category")] Category,
    [JsonStringEnumMemberName("ingredient")] Ingredient,
    [JsonStringEnumMemberName("mapping")] Mapping,
    [JsonStringEnumMemberName("product")] Product,
    [JsonStringEnumMemberName("yield_rule")] YieldRule,
}

public static class Clock
{
    public static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
}

public sealed class Meta
{
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Rev { get; set; }

    public bool ValidOn(DateOnly? d)
    {
        if (d is null) return true;
        if (ValidFrom is { } from && d < from) return false;
        return ValidTo is null || d < ValidTo;
    }
}

public interface IRuleEntity
{
    string Id { get; set; }
    Meta Meta { get; set; }
}

public sealed class Category : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Meta Meta { get; set; } = new();
}

public sealed class Ingredient : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Unit BaseUnit { get; set; }
    public string CategoryId { get; set; } = "";
    public Meta Meta { get; set; } = new();
}

public sealed class ArticleMapping : IRuleEntity
{
    public string Id { get; set; } = "";
    public string? SupplierVatId { get; set; }
    public string? SupplierArticleId { get; set; }
    public string? Gtin { get; set; }
    public string? Name { get; set; }
    // The invoice wording this mapping was made from. Never matched against — it is
    // what the suggester learns a supplier's vocabulary from.
    public string? Observed { get; set; }
    public string? UnitCode { get; set; }
    public string IngredientId { get; set; } = "";
    public long Factor { get; set; }
    public bool Confirmed { get; set; }
    public Meta Meta { get; set; } = new();
}

public static class ArticleName
{
    public static string Canonical(string? s)
    {
        var b = new StringBuilder();
        var gap = false;
        foreach (var c in (s ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == ',')
            {
                if (gap && b.Length > 0) b.Append(' ');
                gap = false;
                b.Append(c);
            }
            else gap = true;
        }
        return b.ToString();
    }
}

public enum OriginKind { Exact, Lexical, Manual }

public static class Match
{
    public static ArticleMapping? Mapping(RuleSet rs, string? supplierVatId, DateOnly? date, InvoiceLine line)
    {
        if (!string.IsNullOrEmpty(line.MappingId) && rs.Mappings.TryGetValue(line.MappingId, out var direct))
            return direct;

        var ids = new List<string>(rs.Mappings.Keys);
        ids.Sort(StringComparer.Ordinal);
        var candidates = new List<ArticleMapping>(ids.Count);
        foreach (var id in ids)
        {
            var m = rs.Mappings[id];
            if (!m.Meta.ValidOn(date)) continue;
            if (!string.IsNullOrEmpty(m.UnitCode) && !string.IsNullOrEmpty(line.UnitCode)
                && !string.Equals(m.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase)) continue;
            candidates.Add(m);
        }

        if (!string.IsNullOrEmpty(supplierVatId) && !string.IsNullOrEmpty(line.SellerArticleId))
            foreach (var m in candidates)
                if (m.SupplierVatId == supplierVatId && m.SupplierArticleId == line.SellerArticleId) return m;

        if (!string.IsNullOrEmpty(line.Gtin))
            foreach (var m in candidates)
                if (m.Gtin == line.Gtin) return m;

        var name = ArticleName.Canonical(line.Name);
        if (name != "")
            foreach (var m in candidates)
                if (ArticleName.Canonical(m.Name) == name) return m;

        return null;
    }

    public static (YieldRule Rule, bool Chosen)? YieldRule(Case c, RuleSet rs, Ingredient ing)
    {
        var byCategory = "";
        foreach (var y in c.Yields)
        {
            if (y.IngredientId == ing.Id && rs.YieldRules.TryGetValue(y.YieldRuleId, out var r)) return (r, true);
            if (string.IsNullOrEmpty(y.IngredientId) && !string.IsNullOrEmpty(y.CategoryId) && y.CategoryId == ing.CategoryId)
                byCategory = y.YieldRuleId;
        }
        if (rs.YieldRules.TryGetValue(byCategory, out var rc)) return (rc, true);

        List<YieldRule> forIngredient = [], forCategory = [];
        foreach (var id in rs.YieldRules.Keys.Order(StringComparer.Ordinal))
        {
            var r = rs.YieldRules[id];
            if (!r.Meta.ValidOn(c.PeriodTo)) continue;
            if (r.IngredientId == ing.Id) forIngredient.Add(r);
            else if (string.IsNullOrEmpty(r.IngredientId) && !string.IsNullOrEmpty(r.CategoryId) && r.CategoryId == ing.CategoryId)
                forCategory.Add(r);
        }
        foreach (var set in new[] { forIngredient, forCategory })
        {
            if (set.Find(r => r.Default) is { } d) return (d, false);
            if (set.Count > 0) return (set[0], false);
        }
        return null;
    }
}

public sealed class RecipeLine
{
    public string IngredientId { get; set; } = "";
    public long Amount { get; set; }
}

public sealed class Product : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<RecipeLine> Recipe { get; set; } = [];
    public Meta Meta { get; set; } = new();
}

public sealed class YieldRule : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CategoryId { get; set; }
    public string? IngredientId { get; set; }
    public long Shrinkage { get; set; }
    public long OwnUse { get; set; }
    public long Staff { get; set; }
    public long Free { get; set; }
    public string Source { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Default { get; set; }
    public Meta Meta { get; set; } = new();
}

public sealed class RuleSet
{
    public long Version { get; set; }
    public Dictionary<string, Category> Categories { get; set; } = [];
    public Dictionary<string, Ingredient> Ingredients { get; set; } = [];
    public Dictionary<string, ArticleMapping> Mappings { get; set; } = [];
    public Dictionary<string, Product> Products { get; set; } = [];
    public Dictionary<string, YieldRule> YieldRules { get; set; } = [];

    public IRuleEntity? Find(Entity entity, string id) => entity switch
    {
        Entity.Category => Categories.GetValueOrDefault(id),
        Entity.Ingredient => Ingredients.GetValueOrDefault(id),
        Entity.Mapping => Mappings.GetValueOrDefault(id),
        Entity.Product => Products.GetValueOrDefault(id),
        Entity.YieldRule => YieldRules.GetValueOrDefault(id),
        _ => null,
    };

    public void Put(IRuleEntity e)
    {
        switch (e)
        {
            case Category x: Categories[x.Id] = x; break;
            case Ingredient x: Ingredients[x.Id] = x; break;
            case ArticleMapping x: Mappings[x.Id] = x; break;
            case Product x: Products[x.Id] = x; break;
            case YieldRule x: YieldRules[x.Id] = x; break;
        }
    }
}

using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class RuleOption(YieldRule rule, string text)
{
    public YieldRule Rule { get; } = rule;
    public string Text { get; } = text;
}

public sealed class YieldKindGroup(string kind, List<YieldGroupRow> rows)
{
    public string Kind { get; } = kind;
    public List<YieldGroupRow> Rows { get; } = rows;
}

public sealed class YieldGroupRow : Observable
{
    RuleOption? selected;

    public YieldGroupRow(YieldChoice choice, string label, List<YieldRule> rules)
    {
        Choice = choice;
        Label = label;
        Options = rules.Select(r => new RuleOption(r, r.Name)).ToList();
    }

    public YieldChoice Choice { get; }
    public string Label { get; }
    public List<RuleOption> Options { get; }
    // The box nulls its selection while it rebinds; a row always has a rule, so ignore that.
    public RuleOption? Selected { get => selected; set { if (value is not null) Set(ref selected, value); } }
}

public static class Yields
{
    // Only what this Prüfung actually touches: an empty café has no business being offered fuel or funerals.
    public static List<YieldKindGroup> Groups(Case k, RuleSet rs, List<Ingredient> ingredients, Func<string, string> categoryName)
    {
        var used = InCase(k, rs);
        var rules = rs.YieldRules.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        List<YieldGroupRow> byCategory = [], byIngredient = [];
        var seen = new HashSet<string>();
        foreach (var ing in ingredients)
        {
            if (!used.Contains(ing.Id)) continue;
            if (ing.CategoryId != "" && seen.Add(ing.CategoryId))
            {
                var forCategory = rules.Where(r => string.IsNullOrEmpty(r.IngredientId) && r.CategoryId == ing.CategoryId).ToList();
                if (forCategory.Count > 1)
                    byCategory.Add(new YieldGroupRow(new YieldChoice { CategoryId = ing.CategoryId }, categoryName(ing.CategoryId), forCategory));
            }
            var forIngredient = rules.Where(r => r.IngredientId == ing.Id).ToList();
            if (forIngredient.Count > 1)
                byIngredient.Add(new YieldGroupRow(new YieldChoice { IngredientId = ing.Id }, ing.Name, forIngredient));
        }
        List<YieldKindGroup> groups = [];
        if (byCategory.Count > 0) groups.Add(new YieldKindGroup("Nach Kategorie", Sorted(byCategory)));
        if (byIngredient.Count > 0) groups.Add(new YieldKindGroup("Nach Zutat", Sorted(byIngredient)));
        foreach (var row in groups.SelectMany(g => g.Rows)) row.Selected = Chosen(k, row);
        return groups;
    }

    public static List<YieldChoice> Choices(IEnumerable<YieldKindGroup> groups) => groups
        .SelectMany(g => g.Rows)
        .Where(r => r.Selected is not null)
        .Select(r => new YieldChoice { IngredientId = r.Choice.IngredientId, CategoryId = r.Choice.CategoryId, YieldRuleId = r.Selected!.Rule.Id })
        .ToList();

    static List<YieldGroupRow> Sorted(List<YieldGroupRow> rows) =>
        rows.OrderBy(r => r.Label, StringComparer.Ordinal).ToList();

    static HashSet<string> InCase(Case k, RuleSet rs)
    {
        var ids = new HashSet<string>();
        foreach (var e in k.Inventory) ids.Add(e.IngredientId);
        var recipes = Recipes.Effective(k, rs);
        foreach (var p in k.Products)
            if (recipes.Products.TryGetValue(p.ProductId, out var product))
                foreach (var line in product.Recipe) ids.Add(line.IngredientId);
        foreach (var invoice in k.Invoices)
            foreach (var line in invoice.Lines)
                if (Match.Mapping(rs, invoice.SupplierName, invoice.Date, line) is { } m) ids.Add(m.IngredientId);
        return ids;
    }

    static RuleOption Chosen(Case k, YieldGroupRow row)
    {
        foreach (var y in k.Yields)
        {
            if (y.IngredientId != row.Choice.IngredientId || y.CategoryId != row.Choice.CategoryId) continue;
            var option = row.Options.Find(o => o.Rule.Id == y.YieldRuleId);
            if (option is not null) return option;
        }
        return row.Options.Find(o => o.Rule.Default) ?? row.Options[0];
    }
}

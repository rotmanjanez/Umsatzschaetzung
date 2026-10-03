using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class RuleOption(YieldRule? rule)
{
    public static readonly RuleOption None = new(null);

    public YieldRule? Rule { get; } = rule;
    public string Text => Rule is null ? "Kein Abzug" : Rule.Default ? Rule.Name + " (Standard)" : Rule.Name;
    public string Deduction => Format.Bp(Rule?.Deduction ?? 0);

    public override string ToString() => $"{Text}, Abzug {Deduction}";
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
        Options = [RuleOption.None, .. rules.Select(r => new RuleOption(r))];
    }

    public YieldChoice Choice { get; }
    public string Label { get; }
    public string Spoken => "Ertragsregel für " + Label;
    public List<RuleOption> Options { get; }
    // The box nulls its selection while it rebinds; a row always has a rule, so ignore that.
    public RuleOption? Selected { get => selected; set { if (value is not null) Set(ref selected, value); } }
}

public static class Yields
{
    // Only what this Prüfung actually touches: an empty café has no business being offered fuel or funerals.
    public static List<YieldKindGroup> Groups(Case k, RuleSet rs, Func<string, string> categoryName)
    {
        var used = InCase(k, rs);
        var rules = rs.YieldRules.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        List<YieldGroupRow> byCategory = [], byProduct = [];
        var seen = new HashSet<string>();
        foreach (var p in rs.Products.Values)
        {
            if (!used.Contains(p.Id)) continue;
            if (p.CategoryId is { Length: > 0 } category && seen.Add(category))
            {
                var forCategory = rules.Where(r => string.IsNullOrEmpty(r.ProductId) && r.CategoryId == category).ToList();
                if (forCategory.Count > 0)
                    byCategory.Add(new YieldGroupRow(new YieldChoice { CategoryId = category }, categoryName(category), forCategory));
            }
            var forProduct = rules.Where(r => r.ProductId == p.Id).ToList();
            if (forProduct.Count > 0)
                byProduct.Add(new YieldGroupRow(new YieldChoice { ProductId = p.Id }, p.Name, forProduct));
        }
        List<YieldKindGroup> groups = [];
        if (byCategory.Count > 0) groups.Add(new YieldKindGroup("Nach Kategorie", Sorted(byCategory)));
        if (byProduct.Count > 0) groups.Add(new YieldKindGroup("Nach Produkt", Sorted(byProduct)));
        foreach (var row in groups.SelectMany(g => g.Rows)) row.Selected = Chosen(k, row);
        return groups;
    }

    public static List<YieldChoice> Choices(IEnumerable<YieldKindGroup> groups) => groups
        .SelectMany(g => g.Rows)
        .Where(r => r.Selected is not null)
        .Select(r => new YieldChoice { ProductId = r.Choice.ProductId, CategoryId = r.Choice.CategoryId, YieldRuleId = r.Selected!.Rule?.Id })
        .ToList();

    static List<YieldGroupRow> Sorted(List<YieldGroupRow> rows) =>
        rows.OrderBy(r => r.Label, StringComparer.Ordinal).ToList();

    static HashSet<string> InCase(Case k, RuleSet rs)
    {
        var ids = new HashSet<string>();
        foreach (var e in k.Inventory) ids.Add(e.ProductId);
        ids.UnionWith(Recipes.Reachable(Recipes.Effective(k, rs), k.Products.Select(p => p.ProductId)));
        foreach (var invoice in k.Invoices)
            foreach (var line in invoice.Lines)
                if (Match.Mapping(rs, invoice.SupplierName, invoice.Date, line) is { } m) ids.Add(m.ProductId);
        return ids;
    }

    static RuleOption Chosen(Case k, YieldGroupRow row)
    {
        foreach (var y in k.Yields)
        {
            if (y.ProductId != row.Choice.ProductId || y.CategoryId != row.Choice.CategoryId) continue;
            if (y.YieldRuleId is null) return RuleOption.None;
            var option = row.Options.Find(o => o.Rule?.Id == y.YieldRuleId);
            if (option is not null) return option;
        }
        return row.Options.Find(o => o.Rule is { Default: true }) ?? RuleOption.None;
    }
}

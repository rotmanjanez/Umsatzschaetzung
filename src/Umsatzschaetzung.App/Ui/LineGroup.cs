using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.App.Ui;

public sealed class LineGroup
{
    public required string Key { get; init; }
    public required string Supplier { get; init; }
    public required string? Article { get; init; }
    public required string Name { get; init; }
    public required string Unit { get; init; }
    public List<(int Invoice, int Line)> Lines { get; } = [];
    public long Quantity { get; set; }
    public string? MappingId { get; set; }
    public Checked State { get; set; } = Checked.Pending;
    public string Search { get; private set; } = "";
    public bool IsAutomatic => State == Checked.Automatic;
    public bool IsManual => State == Checked.Manual;
    public bool IsPending => State == Checked.Pending;
    public string StateText => State switch
    {
        Checked.Automatic => "Automatisch",
        Checked.Manual => "Manuell",
        _ => "Offen",
    };

    // Open positions first, then the machine's decisions, then what a person already settled.
    public int Rank => State == Checked.Pending ? 0 : State == Checked.Automatic ? 1 : 2;

    public static List<LineGroup> Of(Case? c, RuleSet? rs) => c is null ? [] : Of(c.Invoices, c.Mappings, rs, Reached(c, rs));

    static HashSet<string> Reached(Case c, RuleSet? rs) => rs is null ? [] : Scale.Reached(c, Recipes.Effective(c, rs));

    // Copies what an import adds to, for groups made on another thread.
    public static Func<List<LineGroup>> Later(Case? c, RuleSet? rs)
    {
        if (c is null) return () => [];
        List<Invoice> invoices = [.. c.Invoices];
        Dictionary<string, ArticleMapping> mappings = new(c.Mappings);
        var reached = Reached(c, rs);
        return () => Of(invoices, mappings, rs, reached);
    }

    static List<LineGroup> Of(List<Invoice> invoices, Dictionary<string, ArticleMapping> mappings, RuleSet? rs, HashSet<string> reached)
    {
        var groups = new Dictionary<string, LineGroup>();
        rs = rs?.With(mappings);
        for (var i = 0; i < invoices.Count; i++)
        {
            var inv = invoices[i];
            for (var j = 0; j < inv.Lines.Count; j++)
            {
                var l = inv.Lines[j];
                var key = inv.SupplierName + "|" + Identity(inv.SupplierName, l) + "|" + l.UnitCode.ToUpperInvariant();
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new LineGroup { Key = key, Supplier = inv.SupplierName, Article = l.SellerArticleId, Name = l.Name, Unit = l.UnitCode };
                    groups[key] = g;
                }
                g.Lines.Add((i, j));
                g.Quantity += l.Quantity;
            }
        }
        foreach (var g in groups.Values)
        {
            g.State = g.StateOf(invoices, rs, reached);
            g.Search = g.SearchOf(invoices, rs);
        }
        return [.. groups.Values];
    }

    // A group holds exactly the lines one mapping covers: the key Match picks a rule by, and its unit.
    static string Identity(string? supplier, InvoiceLine l) =>
        !string.IsNullOrEmpty(supplier) && !string.IsNullOrEmpty(l.SellerArticleId) ? "a:" + l.SellerArticleId
        : !string.IsNullOrEmpty(l.Gtin) ? "g:" + l.Gtin
        : "n:" + ArticleName.Canonical(l.Name);

    // Besides its own wording a group is found by what it maps to: "Bier" finds the Pils.
    string SearchOf(List<Invoice> invoices, RuleSet? rs)
    {
        var mapped = Lines
            .Select(p => invoices[p.Invoice].Lines[p.Line].MappingId)
            .Select(id => string.IsNullOrEmpty(id) ? null : rs?.Mappings.GetValueOrDefault(id))
            .Select(m => m is null ? null : rs!.Products.GetValueOrDefault(m.ProductId))
            .OfType<Product>()
            .Distinct()
            .Select(i => i.Name + " " + rs!.Categories.GetValueOrDefault(i.CategoryId ?? "")?.Name + " " + string.Join(" ", i.Aliases));
        return string.Join(" ", [Supplier, Name, Article, StateText, .. mapped]);
    }

    // A group is settled by the weakest of its lines: one open line keeps it open, one
    // machine decision keeps it automatic.
    Checked StateOf(List<Invoice> invoices, RuleSet? rs, HashSet<string> reached)
    {
        var state = Checked.Manual;
        foreach (var (inv, line) in Lines)
        {
            var l = invoices[inv].Lines[line];
            if (string.IsNullOrEmpty(l.MappingId) || rs?.Mappings.GetValueOrDefault(l.MappingId) is not { } m) return Checked.Pending;
            if (m.Factor is null && Scale.NeedsFactor(rs, m.ProductId, l, reached)) return Checked.Pending;
            MappingId ??= l.MappingId;
            if (!m.Confirmed) state = Checked.Automatic;
        }
        return state;
    }
}

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

    public static List<LineGroup> Of(Case? c, RuleSet? rs)
    {
        var groups = new Dictionary<string, LineGroup>();
        if (c is null) return [];
        for (var i = 0; i < c.Invoices.Count; i++)
        {
            var inv = c.Invoices[i];
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
        foreach (var g in groups.Values) g.State = g.StateOf(c, rs);
        return [.. groups.Values];
    }

    // A group holds exactly the lines one mapping covers: the key Match picks a rule by, and its unit.
    static string Identity(string? supplier, InvoiceLine l) =>
        !string.IsNullOrEmpty(supplier) && !string.IsNullOrEmpty(l.SellerArticleId) ? "a:" + l.SellerArticleId
        : !string.IsNullOrEmpty(l.Gtin) ? "g:" + l.Gtin
        : "n:" + ArticleName.Canonical(l.Name);

    // A group is settled by the weakest of its lines: one open line keeps it open, one
    // machine decision keeps it automatic.
    Checked StateOf(Case c, RuleSet? rs)
    {
        var state = Checked.Manual;
        foreach (var (inv, line) in Lines)
        {
            var l = c.Invoices[inv].Lines[line];
            if (string.IsNullOrEmpty(l.MappingId) || rs?.Mappings.GetValueOrDefault(l.MappingId) is not { } m) return Checked.Pending;
            if (m.Factor is null && Scale.NeedsFactor(rs, m.IngredientId, l)) return Checked.Pending;
            MappingId ??= l.MappingId;
            if (!m.Confirmed) state = Checked.Automatic;
        }
        return state;
    }
}

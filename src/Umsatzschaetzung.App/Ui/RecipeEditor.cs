using System.Collections.ObjectModel;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

// A product picked from the catalog and a unit it can be counted in.
public class PartRow : Observable
{
    Product? part;
    List<Product> options;
    int unitIndex;
    List<string> codes = [], units = [];

    public PartRow(List<Product> options, string? partId, string? unit)
    {
        this.options = options;
        part = options.Find(p => p.Id == partId);
        Rescale(unit, true);
    }

    public List<Product> Options { get => options; private set => Set(ref options, value); }
    public Product? Part { get => part; set { if (!Set(ref part, value)) return; Rescale(UnitCode, false); Raise(nameof(Title)); } }
    public string Title => part?.Name ?? "neues Produkt";
    public List<string> Units { get => units; private set => Set(ref units, value); }
    // The box drops its index while its items are swapped; a line always has a unit.
    public int UnitIndex { get => unitIndex; set { if (value >= 0 && Set(ref unitIndex, value)) RaiseUnit(); } }
    public string UnitCode => codes[unitIndex];
    // A line stored before its product changed unit keeps its number and says so, rather than reading it in another.
    public bool UnitMismatch => Counted() is { } s && Model.Units.Lookup(UnitCode)?.Base != s;
    public string? UnitHint => UnitMismatch ? $"Das Produkt zählt in {Format.UnitName(Counted()!.Value)}: Menge und Einheit prüfen." : null;

    Unit? Counted() => part is null ? null : Scale.Of(part);

    void RaiseUnit()
    {
        Raise(nameof(UnitMismatch));
        Raise(nameof(UnitHint));
    }

    // New rules hand out new instances; the line keeps its product and its unit.
    public void Offer(List<Product> products)
    {
        var id = part?.Id;
        var unit = UnitCode;
        Options = products;
        part = products.Find(p => p.Id == id);
        Raise(nameof(Part));
        Raise(nameof(Title));
        Rescale(unit, true);
    }

    // Every product whose recipe takes this one in, at any depth: as its part it would close a loop.
    public static HashSet<string> Containing(RuleSet rs, string? id)
    {
        HashSet<string> seen = [];
        if (id is null) return seen;
        var users = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var p in rs.Products.Values)
            foreach (var l in p.Recipe)
            {
                if (!users.TryGetValue(l.PartId, out var list)) users[l.PartId] = list = [];
                list.Add(p.Id);
            }
        var open = new Stack<string>([id]);
        while (open.TryPop(out var next))
            foreach (var user in users.GetValueOrDefault(next, []))
                if (seen.Add(user)) open.Push(user);
        return seen;
    }

    static List<string> Fitting(Unit? scale) =>
        [.. RulesView.RecipeUnits.Where(c => scale is null || Model.Units.Lookup(c)?.Base == scale)];

    // A product picked anew takes a unit it counts in; a stored line keeps its own, fitting or not.
    void Rescale(string? keep, bool stored)
    {
        var scale = Counted();
        codes = Fitting(scale);
        var fits = keep is not null && (scale is null || Model.Units.Lookup(keep)?.Base == scale);
        if (keep is not null && !codes.Contains(keep) && Model.Units.Lookup(keep) is not null && (fits || stored)) codes.Add(keep);
        unitIndex = Math.Max(codes.IndexOf(keep ?? ""), 0);
        Units = [.. codes.Select(Model.Units.Label)];
        Raise(nameof(UnitIndex));
        RaiseUnit();
    }
}

public sealed class CaseRecipeRow(List<Product> options, PartLine? line) : PartRow(options, line?.PartId, line?.Unit)
{
    string amount = line?.Amount.ToString() ?? "", catalogValue = "", note = "";

    public string Amount { get => amount; set { if (!Set(ref amount, value)) return; Raise(nameof(AmountInvalid)); Raise(nameof(AmountHint)); } }
    public bool AmountInvalid => amount.Trim() != "" && Input.Int(amount) is not > 0;
    public string AmountHint => AmountInvalid ? "Ungültig: eine ganze Zahl größer 0 eingeben" : "";
    public string RemoveName => Part is null ? "Produkt entfernen" : $"„{Part.Name}“ entfernen";
    public string CatalogValue { get => catalogValue; set { if (Set(ref catalogValue, value)) Raise(nameof(Differs)); } }
    public bool Differs => catalogValue != "";
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(HasNote)); } }
    public bool HasNote => note != "";

    public PartLine? Line => Part is not null && Input.Int(amount) is long n && n > 0
        ? new PartLine { PartId = Part.Id, Amount = n, Unit = UnitCode }
        : null;
}

public sealed class RecipeEditor(string productId) : Observable
{
    bool adjusted, stale, emptied;
    List<RecipeUse> uses = [];

    public string ProductId { get; } = productId;
    public bool Adjusted { get => adjusted; set => Set(ref adjusted, value); }
    public List<RecipeUse> Uses { get => uses; set => Set(ref uses, value); }
    public ObservableCollection<CaseRecipeRow> Rows { get; } = [];
    public bool Stale { get => stale; set => Set(ref stale, value); }
    public bool Emptied { get => emptied; set => Set(ref emptied, value); }

    public event Action? Edited;

    public static string Amount(PartLine l) =>
        Format.Qty(Scale.ToBase(l.Amount, l.Unit), Model.Units.Lookup(l.Unit)?.Base ?? Unit.Piece);

    public void Add(CaseRecipeRow row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CaseRecipeRow.Part) or nameof(CaseRecipeRow.Amount) or nameof(CaseRecipeRow.UnitIndex))
                Edited?.Invoke();
        };
        Rows.Add(row);
    }

    public List<PartLine> Lines() => [.. Rows.Select(r => r.Line).OfType<PartLine>()];

    // A line is matched to the catalog by its part, else by its place.
    public void Compare(RuleSet catalog, List<PartLine> reference)
    {
        var used = Rows.Select(r => r.Part?.Id).ToHashSet();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var same = reference.Find(l => l.PartId == row.Part?.Id);
            row.CatalogValue =
                row.Part is null ? ""
                : same is not null ? (row.Line is { } l && Amount(l) == Amount(same) ? "" : "Katalog: " + Amount(same))
                : i < reference.Count && !used.Contains(reference[i].PartId) ? "Katalog: " + Names.Product(catalog, reference[i].PartId)
                : "nicht im Katalog";
        }
    }
}

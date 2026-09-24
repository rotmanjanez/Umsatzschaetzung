using System.Collections.ObjectModel;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class CaseRecipeRow : Observable
{
    readonly RuleSet catalog;
    Ingredient? ingredient;
    string amount, catalogValue = "", note = "";
    int unitIndex;
    List<string> codes = [], units = [];

    public CaseRecipeRow(List<Ingredient> options, RuleSet catalog, RecipeLine? line)
    {
        Options = options;
        this.catalog = catalog;
        ingredient = line is null ? null : options.Find(i => i.Id == line.IngredientId);
        amount = line?.Amount.ToString() ?? "";
        Rescale(line?.Unit);
    }

    public List<Ingredient> Options { get; }
    public Ingredient? Ingredient { get => ingredient; set { if (Set(ref ingredient, value)) Rescale(UnitCode); } }
    public string Amount { get => amount; set { if (Set(ref amount, value)) Raise(nameof(AmountInvalid)); } }
    public bool AmountInvalid => amount.Trim() != "" && Input.Int(amount) is not > 0;
    public List<string> Units { get => units; private set => Set(ref units, value); }
    // The box drops its index while its items are swapped; a line always has a unit.
    public int UnitIndex { get => unitIndex; set { if (value >= 0) Set(ref unitIndex, value); } }
    public string UnitCode => codes[unitIndex];
    public string CatalogValue { get => catalogValue; set { if (Set(ref catalogValue, value)) Raise(nameof(Differs)); } }
    public bool Differs => catalogValue != "";
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(HasNote)); } }
    public bool HasNote => note != "";

    public RecipeLine? Line => ingredient is not null && Input.Int(amount) is long n && n > 0
        ? new RecipeLine { IngredientId = ingredient.Id, Amount = n, Unit = UnitCode }
        : null;

    // Only units on the scale the catalog measures this ingredient in; any, if it has none yet.
    void Rescale(string? keep)
    {
        var scale = ingredient is null ? null : Scale.Of(catalog, ingredient.Id);
        bool Fits(string code) => scale is null || Model.Units.Lookup(code)?.Base == scale;
        codes = [.. RulesView.RecipeUnits.Where(Fits)];
        if (keep is not null && !codes.Contains(keep) && Fits(keep)) codes.Add(keep);
        unitIndex = Math.Max(codes.IndexOf(keep ?? ""), 0);
        Units = [.. codes.Select(Model.Units.Label)];
        Raise(nameof(UnitIndex));
    }
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

    public static string Amount(RecipeLine l) =>
        Format.Qty(Scale.ToBase(l.Amount, l.Unit), Model.Units.Lookup(l.Unit)?.Base ?? Unit.Piece);

    public void Add(CaseRecipeRow row)
    {
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(CaseRecipeRow.Ingredient) or nameof(CaseRecipeRow.Amount) or nameof(CaseRecipeRow.UnitIndex))
                Edited?.Invoke();
        };
        Rows.Add(row);
    }

    public List<RecipeLine> Lines() => [.. Rows.Select(r => r.Line).OfType<RecipeLine>()];

    // A line is matched to the catalog by its ingredient, else by its place.
    public void Compare(RuleSet catalog, List<RecipeLine> reference)
    {
        var used = Rows.Select(r => r.Ingredient?.Id).ToHashSet();
        for (var i = 0; i < Rows.Count; i++)
        {
            var row = Rows[i];
            var same = reference.Find(l => l.IngredientId == row.Ingredient?.Id);
            row.CatalogValue =
                row.Ingredient is null ? ""
                : same is not null ? (row.Line is { } l && Amount(l) == Amount(same) ? "" : "Katalog: " + Amount(same))
                : i < reference.Count && !used.Contains(reference[i].IngredientId) ? "Katalog: " + Names.Ingredient(catalog, reference[i].IngredientId)
                : "nicht im Katalog";
        }
    }
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.App.Ui;

public static class RuleLabel
{
    public static string Mark(string name, Meta meta) => meta.ValidTo is null ? name : name + " (zurückgezogen)";
}

public sealed class IngredientItem(Ingredient ingredient)
{
    static readonly Dictionary<Unit, string> Labels = new() { [Unit.Ml] = "ml", [Unit.G] = "g", [Unit.Piece] = "Stück" };

    public Ingredient Ingredient { get; } = ingredient;
    public string Name => RuleLabel.Mark(Ingredient.Name, Ingredient.Meta);
    public string UnitLabel => Labels[Ingredient.BaseUnit];
    public string Category => Ingredient.Category;
}

public sealed class ProductItem(Product product, string recipe)
{
    public Product Product { get; } = product;
    public string Name => RuleLabel.Mark(Product.Name, Product.Meta);
    public string Recipe { get; } = recipe;
}

public sealed class YieldItem(YieldRule rule, string title)
{
    public YieldRule Rule { get; } = rule;
    public string Title { get; } = title;
    public bool ForIngredient => !string.IsNullOrEmpty(Rule.IngredientId);
    public bool ForCategory => !ForIngredient;
    public string Source => Rule.Source;
}

public sealed class RecipeRow(List<Ingredient> options) : Observable
{
    Ingredient? ingredient;
    string amount = "";
    bool ingredientInvalid, amountInvalid;

    public List<Ingredient> Options { get; } = options;
    public Ingredient? Ingredient { get => ingredient; set { if (Set(ref ingredient, value)) IngredientInvalid = false; } }
    public string Amount { get => amount; set { if (Set(ref amount, value)) AmountInvalid = false; } }
    public bool IngredientInvalid { get => ingredientInvalid; set => Set(ref ingredientInvalid, value); }
    public bool AmountInvalid { get => amountInvalid; set => Set(ref amountInvalid, value); }
}

public abstract class EntityForm<T> : Observable
{
    const string WillRetire = "„Zurückziehen“ setzt den Eintrag zum heutigen Tag außer Kraft: Prüfungszeiträume ab diesem Tag "
        + "verwenden ihn nicht mehr, frühere Prüfungen rechnen unverändert damit weiter. Gelöscht wird nichts.";
    const string WasRetired = "Prüfungszeiträume ab diesem Tag verwenden den Eintrag nicht mehr, frühere Prüfungen rechnen "
        + "unverändert damit weiter. Erneutes Speichern macht ihn wieder gültig.";

    string title = "";
    bool existing, active;
    DateOnly? retiredOn;

    public ObservableCollection<T> Items { get; } = [];
    public string? CurrentId { get; set; }
    public string Title { get => title; set => Set(ref title, value); }
    public bool Existing { get => existing; set { if (Set(ref existing, value)) RaiseRetirement(); } }
    public bool Active { get => active; set => Set(ref active, value); }
    public DateOnly? RetiredOn { get => retiredOn; set { if (Set(ref retiredOn, value)) RaiseRetirement(); } }
    public bool Retired => retiredOn is not null;
    public bool Retirable => existing && retiredOn is null;
    public string RetiredLabel => "Zurückgezogen am " + retiredOn?.ToString("dd.MM.yyyy");
    public string RetireHint => retiredOn is null ? WillRetire : WasRetired;

    void RaiseRetirement()
    {
        Raise(nameof(Retired));
        Raise(nameof(Retirable));
        Raise(nameof(RetiredLabel));
        Raise(nameof(RetireHint));
    }
}

public sealed class IngredientForm : EntityForm<IngredientItem>
{
    string name = "", category = "";
    int unitIndex = -1;
    bool nameInvalid, unitInvalid;

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Category { get => category; set => Set(ref category, value); }
    public int UnitIndex { get => unitIndex; set { if (Set(ref unitIndex, value)) UnitInvalid = false; } }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public bool UnitInvalid { get => unitInvalid; set => Set(ref unitInvalid, value); }
}

public sealed class ProductForm : EntityForm<ProductItem>
{
    string name = "";
    bool nameInvalid;

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public ObservableCollection<RecipeRow> Recipe { get; } = [];
}

public sealed class YieldForm : EntityForm<YieldItem>
{
    public static readonly Ingredient None = new() { Id = "", Name = "keine" };

    string name = "", category = "", shrinkage = "", ownUse = "", staff = "", free = "", source = "";
    bool isDefault, nameInvalid, scopeInvalid, sourceInvalid;
    readonly bool[] rateInvalid = new bool[4];
    Ingredient? ingredient = None;
    List<Ingredient> ingredientOptions = [None];

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Category { get => category; set { if (Set(ref category, value)) ScopeInvalid = false; } }
    public string Shrinkage { get => shrinkage; set { if (Set(ref shrinkage, value)) ShrinkageInvalid = false; } }
    public string OwnUse { get => ownUse; set { if (Set(ref ownUse, value)) OwnUseInvalid = false; } }
    public string Staff { get => staff; set { if (Set(ref staff, value)) StaffInvalid = false; } }
    public string Free { get => free; set { if (Set(ref free, value)) FreeInvalid = false; } }
    public string Source { get => source; set { if (Set(ref source, value)) SourceInvalid = false; } }
    public bool IsDefault { get => isDefault; set => Set(ref isDefault, value); }
    public Ingredient? Ingredient { get => ingredient; set { if (Set(ref ingredient, value)) ScopeInvalid = false; } }
    public List<Ingredient> IngredientOptions { get => ingredientOptions; set => Set(ref ingredientOptions, value); }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public bool ScopeInvalid { get => scopeInvalid; set => Set(ref scopeInvalid, value); }
    public bool SourceInvalid { get => sourceInvalid; set => Set(ref sourceInvalid, value); }
    public bool ShrinkageInvalid { get => rateInvalid[0]; set => Set(ref rateInvalid[0], value); }
    public bool OwnUseInvalid { get => rateInvalid[1]; set => Set(ref rateInvalid[1], value); }
    public bool StaffInvalid { get => rateInvalid[2]; set => Set(ref rateInvalid[2], value); }
    public bool FreeInvalid { get => rateInvalid[3]; set => Set(ref rateInvalid[3], value); }

    public void MarkRates(bool invalid)
    {
        ShrinkageInvalid = OwnUseInvalid = StaffInvalid = FreeInvalid = invalid;
    }
}

public sealed class RulesModel
{
    public IngredientForm Ingredients { get; } = new();
    public ProductForm Products { get; } = new();
    public YieldForm Yields { get; } = new();
}

public partial class RulesView : Screen
{
    static readonly Unit[] Units = [Unit.Ml, Unit.G, Unit.Piece];

    readonly RulesModel model = new();
    bool loading;

    public RulesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        IngredientSearch.Attach(model.Ingredients.Items, i => i.Name + " " + i.Category + " " + i.UnitLabel);
        ProductSearch.Attach(model.Products.Items, p => p.Name + " " + p.Recipe);
        YieldSearch.Attach(model.Yields.Items, y => y.Title + " " + y.Source);
        IngredientGrid.ItemsSource = IngredientSearch.View;
        ProductGrid.ItemsSource = ProductSearch.View;
        YieldGrid.ItemsSource = YieldSearch.View;
    }

    protected override async void OnEnter()
    {
        Session.RulesChanged += Rebuild;
        await Session.LoadRules(Ct);
    }

    protected override void OnLeave() => Session.RulesChanged -= Rebuild;

    void Rebuild()
    {
        if (!IsActive || Session.Rules is null) return;
        var rs = Session.Rules.RuleSet;
        loading = true;
        var ingredients = Session.Ingredients();

        model.Ingredients.Items.Clear();
        foreach (var i in ingredients) model.Ingredients.Items.Add(new IngredientItem(i));
        IngredientGrid.SelectedItem = model.Ingredients.Items.FirstOrDefault(i => i.Ingredient.Id == model.Ingredients.CurrentId);

        model.Products.Items.Clear();
        foreach (var p in Session.Products())
            model.Products.Items.Add(new ProductItem(p, Session.Rules.Display.Products.GetValueOrDefault(p.Id)?.Recipe ?? ""));
        ProductGrid.SelectedItem = model.Products.Items.FirstOrDefault(p => p.Product.Id == model.Products.CurrentId);

        model.Yields.IngredientOptions = [YieldForm.None, .. ingredients];
        model.Yields.Items.Clear();
        foreach (var y in rs.YieldRules.Values.OrderBy(y => y.Name, StringComparer.Ordinal))
            model.Yields.Items.Add(new YieldItem(y, RuleLabel.Mark(
                y.Name + " (" + (string.IsNullOrEmpty(y.IngredientId) ? y.Category : Session.IngredientName(y.IngredientId)) + ")", y.Meta)));
        YieldGrid.SelectedItem = model.Yields.Items.FirstOrDefault(y => y.Rule.Id == model.Yields.CurrentId);

        loading = false;
        if (IngredientGrid.SelectedItem is IngredientItem ii) LoadIngredient(ii.Ingredient); else model.Ingredients.Active = false;
        if (ProductGrid.SelectedItem is ProductItem pi) LoadProduct(pi.Product); else model.Products.Active = false;
        if (YieldGrid.SelectedItem is YieldItem yi) LoadYield(yi.Rule); else model.Yields.Active = false;
    }

    static string Missing(params string?[] fields) =>
        "Bitte prüfen: " + string.Join(", ", fields.OfType<string>()) + ".";

    async Task Retire(Entity entity, string? id, string noun, string name)
    {
        if (id is null) return;
        var confirmed = await Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "„" + name + "“ wird zum heutigen Tag außer Kraft gesetzt: Prüfungszeiträume ab heute ziehen den Eintrag "
            + "nicht mehr heran, frühere Prüfungen rechnen unverändert damit weiter. Gelöscht wird nichts – der Eintrag "
            + "bleibt in der Liste und wird dort als zurückgezogen geführt.",
            noun + " zurückziehen");
        if (!confirmed) return;
        await Session.Retire(entity, id, Ct);
    }

    void IngredientSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!loading && IngredientGrid.SelectedItem is IngredientItem item) LoadIngredient(item.Ingredient);
    }

    void LoadIngredient(Ingredient i)
    {
        var f = model.Ingredients;
        f.CurrentId = i.Id;
        f.Existing = f.Active = true;
        f.RetiredOn = i.Meta.ValidTo;
        f.Title = i.Name;
        f.Name = i.Name;
        f.UnitIndex = Array.IndexOf(Units, i.BaseUnit);
        f.Category = i.Category;
    }

    void NewIngredient(object? sender, RoutedEventArgs e)
    {
        var f = model.Ingredients;
        IngredientGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.RetiredOn = null;
        f.Title = "Neue Zutat";
        f.Name = "";
        f.UnitIndex = -1;
        f.Category = "";
    }

    async void SaveIngredient(object? sender, RoutedEventArgs e)
    {
        var f = model.Ingredients;
        f.NameInvalid = f.Name.Trim() == "";
        f.UnitInvalid = f.UnitIndex < 0;
        if (f.NameInvalid || f.UnitInvalid)
        {
            Session.Fail(Missing(f.NameInvalid ? "Name" : null, f.UnitInvalid ? "Basiseinheit" : null));
            return;
        }
        var id = f.CurrentId ?? Session.NewId("ingredient");
        var data = new Ingredient { Id = id, Name = f.Name.Trim(), BaseUnit = Units[f.UnitIndex], Category = f.Category.Trim() };
        f.CurrentId = id;
        await Session.Put(data, Ct);
    }

    async void RetireIngredient(object? sender, RoutedEventArgs e)
    {
        await Retire(Entity.Ingredient, model.Ingredients.CurrentId, "Zutat", model.Ingredients.Title);
    }

    void ProductSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!loading && ProductGrid.SelectedItem is ProductItem item) LoadProduct(item.Product);
    }

    void LoadProduct(Product p)
    {
        var f = model.Products;
        var options = Session.Ingredients();
        f.CurrentId = p.Id;
        f.Existing = f.Active = true;
        f.RetiredOn = p.Meta.ValidTo;
        f.Title = p.Name;
        f.Name = p.Name;
        f.Recipe.Clear();
        foreach (var l in p.Recipe)
            f.Recipe.Add(new RecipeRow(options) { Ingredient = options.Find(i => i.Id == l.IngredientId), Amount = l.Amount.ToString() });
    }

    void NewProduct(object? sender, RoutedEventArgs e)
    {
        var f = model.Products;
        ProductGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.RetiredOn = null;
        f.Title = "Neues Produkt";
        f.Name = "";
        f.Recipe.Clear();
    }

    void AddRecipeLine(object? sender, RoutedEventArgs e) => model.Products.Recipe.Add(new RecipeRow(Session.Ingredients()));

    void RemoveRecipeLine(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RecipeRow row) model.Products.Recipe.Remove(row);
    }

    async void SaveProduct(object? sender, RoutedEventArgs e)
    {
        var f = model.Products;
        var id = f.CurrentId ?? Session.NewId("product");
        var data = new Product { Id = id, Name = f.Name.Trim() };
        f.NameInvalid = data.Name == "";
        var lines = true;
        foreach (var row in f.Recipe)
        {
            var amount = Input.Int(row.Amount);
            row.IngredientInvalid = row.Ingredient is null;
            row.AmountInvalid = amount is not > 0;
            if (row.IngredientInvalid || row.AmountInvalid) lines = false;
            else data.Recipe.Add(new RecipeLine { IngredientId = row.Ingredient!.Id, Amount = amount!.Value });
        }
        if (f.NameInvalid || !lines || data.Recipe.Count == 0)
        {
            Session.Fail(Missing(
                f.NameInvalid ? "Name" : null,
                f.Recipe.Count == 0 ? "Rezept (mindestens eine Zutat)"
                    : lines ? null : "Rezept (Zutat und Menge je Portion, Menge größer als 0)"));
            return;
        }
        f.CurrentId = id;
        await Session.Put(data, Ct);
    }

    async void RetireProduct(object? sender, RoutedEventArgs e)
    {
        await Retire(Entity.Product, model.Products.CurrentId, "Produkt", model.Products.Title);
    }

    void YieldSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!loading && YieldGrid.SelectedItem is YieldItem item) LoadYield(item.Rule);
    }

    void LoadYield(YieldRule y)
    {
        var f = model.Yields;
        f.CurrentId = y.Id;
        f.Existing = f.Active = true;
        f.RetiredOn = y.Meta.ValidTo;
        f.Title = y.Name;
        f.Name = y.Name;
        f.IsDefault = y.Default;
        f.Category = y.Category ?? "";
        f.Ingredient = f.IngredientOptions.Find(i => i.Id == (y.IngredientId ?? "")) ?? YieldForm.None;
        f.Shrinkage = Input.BpText(y.Shrinkage);
        f.OwnUse = Input.BpText(y.OwnUse);
        f.Staff = Input.BpText(y.Staff);
        f.Free = Input.BpText(y.Free);
        f.Source = y.Source;
    }

    void NewYield(object? sender, RoutedEventArgs e)
    {
        var f = model.Yields;
        YieldGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.RetiredOn = null;
        f.Title = "Neue Ertragsregel";
        f.Name = f.Category = f.Shrinkage = f.OwnUse = f.Staff = f.Free = f.Source = "";
        f.IsDefault = false;
        f.Ingredient = YieldForm.None;
    }

    async void SaveYield(object? sender, RoutedEventArgs e)
    {
        var f = model.Yields;
        var id = f.CurrentId ?? Session.NewId("yield_rule");
        var rates = new[] { Input.Bp(f.Shrinkage), Input.Bp(f.OwnUse), Input.Bp(f.Staff), Input.Bp(f.Free) };
        var ingredientId = f.Ingredient is { Id: not "" } ing ? ing.Id : null;
        var category = f.Category.Trim() == "" ? null : f.Category.Trim();
        f.NameInvalid = f.Name.Trim() == "";
        f.ScopeInvalid = ingredientId is null && category is null;
        f.SourceInvalid = f.Source.Trim() == "";
        f.ShrinkageInvalid = rates[0] is not >= 0;
        f.OwnUseInvalid = rates[1] is not >= 0;
        f.StaffInvalid = rates[2] is not >= 0;
        f.FreeInvalid = rates[3] is not >= 0;
        var rated = rates.All(r => r is >= 0);
        var over = rated && rates.Sum(r => r!.Value) > Bp.Full;
        if (over) f.MarkRates(true);
        if (f.NameInvalid || f.ScopeInvalid || f.SourceInvalid || !rated || over)
        {
            Session.Fail(Missing(
                f.NameInvalid ? "Name" : null,
                f.ScopeInvalid ? "Kategorie oder Zutat" : null,
                rated ? null : "Anteile (Prozentwerte, nicht negativ)",
                over ? "Anteile (zusammen höchstens 100 %)" : null,
                f.SourceInvalid ? "Quelle" : null));
            return;
        }
        var data = new YieldRule
        {
            Id = id,
            Name = f.Name.Trim(),
            Default = f.IsDefault,
            Category = category,
            IngredientId = ingredientId,
            Shrinkage = rates[0]!.Value,
            OwnUse = rates[1]!.Value,
            Staff = rates[2]!.Value,
            Free = rates[3]!.Value,
            Source = f.Source.Trim(),
        };
        f.CurrentId = id;
        await Session.Put(data, Ct);
    }

    async void RetireYield(object? sender, RoutedEventArgs e)
    {
        await Retire(Entity.YieldRule, model.Yields.CurrentId, "Ertragsregel", model.Yields.Title);
    }
}

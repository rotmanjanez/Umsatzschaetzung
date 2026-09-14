using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.App.Ui;

public sealed class IngredientItem(Ingredient ingredient)
{
    static readonly Dictionary<Unit, string> Labels = new() { [Unit.Ml] = "ml", [Unit.G] = "g", [Unit.Piece] = "Stück" };

    public Ingredient Ingredient { get; } = ingredient;
    public string Name => Ingredient.Name;
    public string UnitLabel => Labels[Ingredient.BaseUnit];
    public string Category => Ingredient.Category;
}

public sealed class ProductItem(Product product, string recipe)
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
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

    public List<Ingredient> Options { get; } = options;
    public Ingredient? Ingredient { get => ingredient; set => Set(ref ingredient, value); }
    public string Amount { get => amount; set => Set(ref amount, value); }
}

public abstract class EntityForm<T> : Observable
{
    string title = "";
    bool existing;

    public ObservableCollection<T> Items { get; } = [];
    public string? CurrentId { get; set; }
    public string Title { get => title; set => Set(ref title, value); }
    public bool Existing { get => existing; set => Set(ref existing, value); }
}

public sealed class IngredientForm : EntityForm<IngredientItem>
{
    string name = "", category = "";
    int unitIndex = -1;

    public string Name { get => name; set => Set(ref name, value); }
    public string Category { get => category; set => Set(ref category, value); }
    public int UnitIndex { get => unitIndex; set => Set(ref unitIndex, value); }
}

public sealed class ProductForm : EntityForm<ProductItem>
{
    string name = "";

    public string Name { get => name; set => Set(ref name, value); }
    public ObservableCollection<RecipeRow> Recipe { get; } = [];
}

public sealed class YieldForm : EntityForm<YieldItem>
{
    public static readonly Ingredient None = new() { Id = "", Name = "keine" };

    string name = "", category = "", shrinkage = "", ownUse = "", staff = "", free = "", source = "";
    bool isDefault;
    Ingredient? ingredient = None;
    List<Ingredient> ingredientOptions = [None];

    public string Name { get => name; set => Set(ref name, value); }
    public string Category { get => category; set => Set(ref category, value); }
    public string Shrinkage { get => shrinkage; set => Set(ref shrinkage, value); }
    public string OwnUse { get => ownUse; set => Set(ref ownUse, value); }
    public string Staff { get => staff; set => Set(ref staff, value); }
    public string Free { get => free; set => Set(ref free, value); }
    public string Source { get => source; set => Set(ref source, value); }
    public bool IsDefault { get => isDefault; set => Set(ref isDefault, value); }
    public Ingredient? Ingredient { get => ingredient; set => Set(ref ingredient, value); }
    public List<Ingredient> IngredientOptions { get => ingredientOptions; set => Set(ref ingredientOptions, value); }
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
            model.Yields.Items.Add(new YieldItem(y, y.Name + " (" + (string.IsNullOrEmpty(y.IngredientId) ? y.Category : Session.IngredientName(y.IngredientId)) + ")"));
        YieldGrid.SelectedItem = model.Yields.Items.FirstOrDefault(y => y.Rule.Id == model.Yields.CurrentId);

        loading = false;
        if (IngredientGrid.SelectedItem is IngredientItem ii) LoadIngredient(ii.Ingredient); else NewIngredient(this, new RoutedEventArgs());
        if (ProductGrid.SelectedItem is ProductItem pi) LoadProduct(pi.Product); else NewProduct(this, new RoutedEventArgs());
        if (YieldGrid.SelectedItem is YieldItem yi) LoadYield(yi.Rule); else NewYield(this, new RoutedEventArgs());
    }

    async Task Retire(Entity entity, string? id, string noun, string name)
    {
        if (id is null) return;
        var answer = MessageBox.Show("„" + name + "“ gilt danach nicht mehr für neue Berechnungen.", noun + " zurückziehen",
            MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes) return;
        await Session.Retire(entity, id, Ct);
    }

    void IngredientSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && IngredientGrid.SelectedItem is IngredientItem item) LoadIngredient(item.Ingredient);
    }

    void LoadIngredient(Ingredient i)
    {
        var f = model.Ingredients;
        f.CurrentId = i.Id;
        f.Existing = true;
        f.Title = i.Name;
        f.Name = i.Name;
        f.UnitIndex = Array.IndexOf(Units, i.BaseUnit);
        f.Category = i.Category;
    }

    void NewIngredient(object sender, RoutedEventArgs e)
    {
        var f = model.Ingredients;
        IngredientGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Title = "Neue Zutat";
        f.Name = "";
        f.UnitIndex = -1;
        f.Category = "";
    }

    async void SaveIngredient(object sender, RoutedEventArgs e)
    {
        var f = model.Ingredients;
        if (f.Name.Trim() == "" || f.UnitIndex < 0)
        {
            Session.Fail("Bitte alle Pflichtfelder ausfüllen.");
            return;
        }
        var id = f.CurrentId ?? Session.NewId("ingredient");
        var data = new Ingredient { Id = id, Name = f.Name.Trim(), BaseUnit = Units[f.UnitIndex], Category = f.Category.Trim() };
        f.CurrentId = id;
        await Session.Put(data, Ct);
    }

    async void RetireIngredient(object sender, RoutedEventArgs e)
    {
        await Retire(Entity.Ingredient, model.Ingredients.CurrentId, "Zutat", model.Ingredients.Title);
        model.Ingredients.CurrentId = null;
    }

    void ProductSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && ProductGrid.SelectedItem is ProductItem item) LoadProduct(item.Product);
    }

    void LoadProduct(Product p)
    {
        var f = model.Products;
        var options = Session.Ingredients();
        f.CurrentId = p.Id;
        f.Existing = true;
        f.Title = p.Name;
        f.Name = p.Name;
        f.Recipe.Clear();
        foreach (var l in p.Recipe)
            f.Recipe.Add(new RecipeRow(options) { Ingredient = options.Find(i => i.Id == l.IngredientId), Amount = l.Amount.ToString() });
    }

    void NewProduct(object sender, RoutedEventArgs e)
    {
        var f = model.Products;
        ProductGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Title = "Neues Produkt";
        f.Name = "";
        f.Recipe.Clear();
    }

    void AddRecipeLine(object sender, RoutedEventArgs e) => model.Products.Recipe.Add(new RecipeRow(Session.Ingredients()));

    void RemoveRecipeLine(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is RecipeRow row) model.Products.Recipe.Remove(row);
    }

    async void SaveProduct(object sender, RoutedEventArgs e)
    {
        var f = model.Products;
        var id = f.CurrentId ?? Session.NewId("product");
        var data = new Product { Id = id, Name = f.Name.Trim() };
        foreach (var row in f.Recipe)
        {
            var amount = Input.Int(row.Amount);
            if (row.Ingredient is null || amount is null)
            {
                Session.Fail("Bitte alle Pflichtfelder ausfüllen.");
                return;
            }
            data.Recipe.Add(new RecipeLine { IngredientId = row.Ingredient.Id, Amount = amount.Value });
        }
        if (data.Name == "")
        {
            Session.Fail("Bitte alle Pflichtfelder ausfüllen.");
            return;
        }
        f.CurrentId = id;
        await Session.Put(data, Ct);
    }

    async void RetireProduct(object sender, RoutedEventArgs e)
    {
        await Retire(Entity.Product, model.Products.CurrentId, "Produkt", model.Products.Title);
        model.Products.CurrentId = null;
    }

    void YieldSelected(object sender, SelectionChangedEventArgs e)
    {
        if (!loading && YieldGrid.SelectedItem is YieldItem item) LoadYield(item.Rule);
    }

    void LoadYield(YieldRule y)
    {
        var f = model.Yields;
        f.CurrentId = y.Id;
        f.Existing = true;
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

    void NewYield(object sender, RoutedEventArgs e)
    {
        var f = model.Yields;
        YieldGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Title = "Neue Ertragsregel";
        f.Name = f.Category = f.Shrinkage = f.OwnUse = f.Staff = f.Free = f.Source = "";
        f.IsDefault = false;
        f.Ingredient = YieldForm.None;
    }

    async void SaveYield(object sender, RoutedEventArgs e)
    {
        var f = model.Yields;
        var id = f.CurrentId ?? Session.NewId("yield_rule");
        var rates = new[] { Input.Bp(f.Shrinkage), Input.Bp(f.OwnUse), Input.Bp(f.Staff), Input.Bp(f.Free) };
        var ingredientId = f.Ingredient is { Id: not "" } ing ? ing.Id : null;
        var category = f.Category.Trim() == "" ? null : f.Category.Trim();
        if (f.Name.Trim() == "" || rates.Any(r => r is null) || (ingredientId is null && category is null))
        {
            Session.Fail("Bitte alle Pflichtfelder ausfüllen.");
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

    async void RetireYield(object sender, RoutedEventArgs e)
    {
        await Retire(Entity.YieldRule, model.Yields.CurrentId, "Ertragsregel", model.Yields.Title);
        model.Yields.CurrentId = null;
    }
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class IngredientItem(Ingredient ingredient, string category)
{
    public Ingredient Ingredient { get; } = ingredient;
    public string Name => Ingredient.Name;
    public string Category { get; } = category;
    public string Aliases => string.Join(", ", Ingredient.Aliases);
    public string AliasCount => Ingredient.Aliases.Count switch
    {
        0 => "",
        1 => "1 Warenart",
        var n => n + " Warenarten",
    };
    public string Search => Name + " " + Category + " " + Aliases;
    public string Tip => string.Join("\n", new[] { Category == "" ? Name : Name + " · " + Category, Aliases }.Where(t => t != ""));
}

public sealed class CategoryOption(string? id, string name)
{
    // Id null marks the entry that creates a category instead of choosing one.
    public string? Id { get; } = id;
    public string Name { get; } = name;
}

public sealed class CategoryPicker : Observable
{
    public static readonly CategoryOption Create = new(null, "+ neue Kategorie …");
    public static readonly CategoryOption None = new("", "keine");

    CategoryOption? selected = None;
    List<CategoryOption> options = [None, Create];
    string newName = "";
    bool invalid;

    public List<CategoryOption> Options { get => options; private set => Set(ref options, value); }
    public string NewName { get => newName; set { if (Set(ref newName, value)) Invalid = false; } }
    public bool Invalid { get => invalid; set => Set(ref invalid, value); }
    public bool Creating => ReferenceEquals(selected, Create);

    public CategoryOption? Selected
    {
        get => selected;
        set
        {
            if (!Set(ref selected, value)) return;
            Invalid = false;
            Raise(nameof(Creating));
        }
    }

    public void Load(List<Category> categories, string? id)
    {
        Options = [None, .. categories.Select(c => new CategoryOption(c.Id, c.Name)), Create];
        NewName = "";
        Selected = Options.Find(o => o.Id == (id ?? "")) ?? None;
    }
}

public sealed class ProductItem(Product product, string recipe)
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
    public string Recipe { get; } = recipe;
    public string Tip => Recipe == "" ? Name : Name + "\n" + Recipe;
}

public sealed class YieldItem(YieldRule rule)
{
    public YieldRule Rule { get; } = rule;
    public string Title => Rule.Name;
    public string Standard => Rule.Default ? "Standard" : "";
}

// A row of the left column: the category (or ingredient) whose rules are alternatives,
// only one of which is in force at a time.
public sealed class ScopeItem(string id, bool ingredient, string label, string search, List<YieldItem> rules)
{
    public string Id { get; } = id;
    public bool Ingredient { get; } = ingredient;
    public string Label { get; } = label;
    public string Search { get; } = search;
    public List<YieldItem> Rules { get; } = rules;
    public string Kind => Ingredient ? "Zutat" : "Kategorie";
    public string Tip => Kind + " · " + Rules.Count + (Rules.Count == 1 ? " Regel" : " Regeln");
}

public sealed class RecipeRow(List<Ingredient> options) : Observable
{
    Ingredient? ingredient;
    string amount = "";
    int unitIndex;
    bool ingredientInvalid, amountInvalid;

    public List<Ingredient> Options { get; } = options;
    public List<string> Units { get; } = [.. RulesView.RecipeUnits.Select(c => Model.Units.Label(c))];
    public Ingredient? Ingredient
    {
        get => ingredient;
        set { if (Set(ref ingredient, value)) IngredientInvalid = false; }
    }
    public int UnitIndex { get => unitIndex; set => Set(ref unitIndex, value); }
    public string Amount { get => amount; set { if (Set(ref amount, value)) AmountInvalid = false; } }
    public bool IngredientInvalid { get => ingredientInvalid; set => Set(ref ingredientInvalid, value); }
    public bool AmountInvalid { get => amountInvalid; set => Set(ref amountInvalid, value); }
}

public abstract class EntityForm : Observable
{
    string title = "";
    bool existing, active;

    public string? CurrentId { get; set; }
    public string Title { get => title; set => Set(ref title, value); }
    public bool Existing { get => existing; set => Set(ref existing, value); }
    public bool Active { get => active; set => Set(ref active, value); }
}

public abstract class EntityForm<T> : EntityForm
{
    public ObservableCollection<T> Items { get; } = [];
}

public sealed class IngredientForm : EntityForm<IngredientItem>
{
    public static readonly Unit[] PieceUnits = [Unit.G, Unit.Ml];

    string name = "", aliases = "", piece = "";
    int pieceUnitIndex;
    bool nameInvalid, pieceInvalid, pieceUnitFree = true;

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Aliases { get => aliases; set => Set(ref aliases, value); }
    public CategoryPicker Category { get; } = new();
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public string Piece { get => piece; set { if (Set(ref piece, value)) PieceInvalid = false; } }
    public List<string> PieceUnitNames { get; } = [.. PieceUnits.Select(Format.UnitName)];
    public int PieceUnitIndex { get => pieceUnitIndex; set => Set(ref pieceUnitIndex, value); }
    // The recipes fix g or ml; only an ingredient counted in pieces, or in none yet, may choose.
    public bool PieceUnitFree { get => pieceUnitFree; set => Set(ref pieceUnitFree, value); }
    public bool PieceInvalid { get => pieceInvalid; set => Set(ref pieceInvalid, value); }

    public void LoadPiece(Piece? p, Unit? recipe)
    {
        Piece = p is null ? "" : Format.Group(p.Amount);
        var fixedUnit = recipe is Unit.G or Unit.Ml ? recipe : null;
        PieceUnitIndex = Math.Max(Array.IndexOf(PieceUnits, fixedUnit ?? p?.Unit ?? Unit.G), 0);
        PieceUnitFree = fixedUnit is null;
    }
}

public sealed class ProductForm : EntityForm<ProductItem>
{
    string name = "";
    bool nameInvalid;

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public ObservableCollection<RecipeRow> Recipe { get; } = [];
}

public sealed class YieldForm : EntityForm<ScopeItem>
{
    public static readonly Ingredient None = new() { Id = "", Name = "keine" };

    string scopeTitle = "", name = "", shrinkage = "", ownUse = "", staff = "", free = "";
    int scopeIndex;
    bool isDefault, nameInvalid, scopeInvalid;
    readonly bool[] rateInvalid = new bool[4];
    Ingredient? ingredient = None;
    List<Ingredient> ingredientOptions = [None];

    public ObservableCollection<YieldItem> Rules { get; } = [];
    public string ScopeTitle { get => scopeTitle; set => Set(ref scopeTitle, value); }
    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public CategoryPicker Category { get; } = new();

    public int ScopeIndex
    {
        get => scopeIndex;
        set
        {
            if (!Set(ref scopeIndex, value)) return;
            ScopeInvalid = false;
            Raise(nameof(ScopeIsIngredient));
            Raise(nameof(ScopeKind));
        }
    }
    public bool ScopeIsIngredient => scopeIndex == 1;
    public string ScopeKind => ScopeIsIngredient ? "Zutat" : "Kategorie";

    public string Shrinkage { get => shrinkage; set { if (Set(ref shrinkage, value)) ShrinkageInvalid = false; } }
    public string OwnUse { get => ownUse; set { if (Set(ref ownUse, value)) OwnUseInvalid = false; } }
    public string Staff { get => staff; set { if (Set(ref staff, value)) StaffInvalid = false; } }
    public string Free { get => free; set { if (Set(ref free, value)) FreeInvalid = false; } }
    public bool IsDefault { get => isDefault; set => Set(ref isDefault, value); }
    public Ingredient? Ingredient { get => ingredient; set { if (Set(ref ingredient, value)) ScopeInvalid = false; } }
    public List<Ingredient> IngredientOptions { get => ingredientOptions; set => Set(ref ingredientOptions, value); }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public bool ScopeInvalid { get => scopeInvalid; set => Set(ref scopeInvalid, value); }
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
    public static readonly string[] RecipeUnits = ["GRM", "KGM", "MLT", "LTR", "H87"];

    readonly RulesModel model = new();
    bool loading, saving;
    Action<string>? productCreated;

    public override string Topic => Tabs.SelectedIndex switch
    {
        1 => Help.Rules + "#produkte",
        2 => Help.Rules + "#ertragsregeln",
        _ => Help.Rules + "#zutaten",
    };

    public RulesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        IngredientSearch.Attach(model.Ingredients.Items, i => i.Search);
        ProductSearch.Attach(model.Products.Items, p => p.Name + " " + p.Recipe);
        YieldSearch.Attach(model.Yields.Items, s => s.Search);
        IngredientGrid.ItemsSource = IngredientSearch.View;
        ProductGrid.ItemsSource = ProductSearch.View;
        ScopeGrid.ItemsSource = YieldSearch.View;
    }

    protected override async void OnEnter()
    {
        Session.RulesChanged += Rebuild;
        await Session.LoadRules(Ct);
    }

    protected override void OnLeave() => Session.RulesChanged -= Rebuild;

    void Rebuild()
    {
        if (saving || !IsActive || Session.Rules is null) return;
        var rs = Session.Rules;
        loading = true;
        var ingredients = Session.Ingredients();

        IngredientBox.SetCategoryNames(this, Session.CategoryNames);
        IngredientBox.SetSimilar(this, Session.SimilarIngredients);

        model.Ingredients.Items.Clear();
        foreach (var i in ingredients) model.Ingredients.Items.Add(new IngredientItem(i, Session.CategoryName(i.CategoryId)));
        IngredientGrid.SelectedItem = model.Ingredients.Items.FirstOrDefault(i => i.Ingredient.Id == model.Ingredients.CurrentId);

        model.Products.Items.Clear();
        foreach (var p in Session.Products())
            model.Products.Items.Add(new ProductItem(p, Names.Recipe(rs, p)));
        ProductGrid.SelectedItem = model.Products.Items.FirstOrDefault(p => p.Product.Id == model.Products.CurrentId);

        model.Yields.IngredientOptions = [YieldForm.None, .. ingredients];
        var scopes = YieldScopes(rs);
        model.Yields.Items.Clear();
        foreach (var scope in scopes) model.Yields.Items.Add(scope);
        ScopeGrid.SelectedItem = scopes.Find(s => s.Rules.Exists(r => r.Rule.Id == model.Yields.CurrentId)) ?? scopes.FirstOrDefault();

        loading = false;
        if (IngredientGrid.SelectedItem is IngredientItem ii) LoadIngredient(ii.Ingredient); else model.Ingredients.Active = false;
        if (ProductGrid.SelectedItem is ProductItem pi) LoadProduct(pi.Product);
        else if (model.Products.CurrentId is not null) model.Products.Active = false;
        if (ScopeGrid.SelectedItem is ScopeItem si) ShowScope(si, model.Yields.CurrentId); else model.Yields.Active = false;
    }

    static string Missing(params string?[] fields) =>
        "Bitte prüfen: " + string.Join(", ", fields.OfType<string>()) + ".";

    async Task Delete(EntityForm form, Entity entity)
    {
        if (form.CurrentId is not { } id) return;
        var noun = Format.EntityName(entity);
        var confirmed = await Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "„" + form.Title + "“ wird dauerhaft aus den Regeln entfernt. Bereits erstellte Berichte bleiben unverändert.",
            noun + " löschen");
        if (!confirmed) return;
        if (await Session.Delete(entity, id, Ct)) form.CurrentId = null;
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
        f.Title = i.Name;
        f.Name = i.Name;
        f.Aliases = string.Join(Environment.NewLine, i.Aliases);
        f.LoadPiece(i.Piece, Session.Rules is { } rs ? Scale.Of(rs, i.Id) : null);
        f.Category.Load(Session.Categories(), i.CategoryId);
    }

    void NewIngredient(object? sender, RoutedEventArgs e)
    {
        var f = model.Ingredients;
        IngredientGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neue Zutat";
        f.Name = f.Aliases = "";
        f.LoadPiece(null, null);
        f.Category.Load(Session.Categories(), null);
    }

    async void SaveIngredient(object? sender, RoutedEventArgs e)
    {
        var f = model.Ingredients;
        var weight = Input.Int(f.Piece);
        f.NameInvalid = f.Name.Trim() == "";
        f.PieceInvalid = f.Piece.Trim() != "" && weight is not > 0;
        if (f.NameInvalid || f.PieceInvalid)
        {
            Session.Fail(Missing(f.NameInvalid ? "Name" : null, f.PieceInvalid ? "Stückgewicht (größer als 0)" : null));
            return;
        }
        var id = f.CurrentId ?? Session.NewId("ingredient");
        var data = new Ingredient
        {
            Id = id, Name = f.Name.Trim(), Aliases = AliasLines(f.Aliases),
            Piece = weight is { } w ? new Piece(w, IngredientForm.PieceUnits[f.PieceUnitIndex]) : null,
        };
        await Compose(async () =>
        {
            if (await CategoryId(f.Category) is not { } categoryId) return;
            data.CategoryId = categoryId;
            f.CurrentId = id;
            await Session.Put(data, Ct);
        });
    }

    static List<string> AliasLines(string text) =>
        [.. text.Split('\n').Select(a => a.Trim()).Where(a => a != "")];

    async void DeleteIngredient(object? sender, RoutedEventArgs e) => await Delete(model.Ingredients, Entity.Ingredient);

    void ProductSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!loading && ProductGrid.SelectedItem is ProductItem item) LoadProduct(item.Product);
    }

    void LoadProduct(Product p)
    {
        var f = model.Products;
        productCreated = null;
        var options = Session.Ingredients();
        f.CurrentId = p.Id;
        f.Existing = f.Active = true;
        f.Title = p.Name;
        f.Name = p.Name;
        f.Recipe.Clear();
        foreach (var l in p.Recipe)
            f.Recipe.Add(new RecipeRow(options)
            {
                Ingredient = options.Find(i => i.Id == l.IngredientId),
                Amount = l.Amount.ToString(),
                UnitIndex = Math.Max(Array.IndexOf(RecipeUnits, l.Unit), 0),
            });
    }

    void NewProduct(object? sender, RoutedEventArgs e) => NewProduct("", null);

    public void NewProduct(string name, Action<string>? created)
    {
        var f = model.Products;
        Tabs.SelectedIndex = 1;
        ProductGrid.SelectedItem = null;
        productCreated = created;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neues Produkt";
        f.Name = name;
        f.Recipe.Clear();
        if (created is not null) f.Recipe.Add(new RecipeRow(Session.Ingredients()));
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
            else data.Recipe.Add(new RecipeLine { IngredientId = row.Ingredient!.Id, Amount = amount!.Value, Unit = RecipeUnits[row.UnitIndex] });
        }
        if (f.NameInvalid || !lines || data.Recipe.Count == 0)
        {
            Session.Fail(Missing(
                f.NameInvalid ? "Name" : null,
                f.Recipe.Count == 0 ? "Rezept (mindestens eine Zutat)"
                    : lines ? null : "Rezept (Zutat und Menge je Portion, Menge größer als 0)"));
            return;
        }
        var isNew = f.CurrentId is null;
        f.CurrentId = id;
        if (await Session.Put(data, Ct) && isNew && productCreated is { } created)
        {
            productCreated = null;
            created(id);
        }
    }

    async void DeleteProduct(object? sender, RoutedEventArgs e) => await Delete(model.Products, Entity.Product);

    // A new category is a second entity: hold the rebuild so the half-filled form survives both saves.
    async Task Compose(Func<Task> save)
    {
        saving = true;
        try
        {
            await save();
        }
        finally
        {
            saving = false;
        }
        Rebuild();
    }

    // "" when no category is wanted, the id otherwise, null when the picker is not usable.
    async Task<string?> CategoryId(CategoryPicker picker)
    {
        if (!picker.Creating) return picker.Selected?.Id ?? "";
        var name = picker.NewName.Trim();
        picker.Invalid = name == "";
        if (picker.Invalid)
        {
            Session.Fail(Missing("Name der neuen Kategorie"));
            return null;
        }
        if (Session.Categories().Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return existing.Id;
        var id = Session.NewId("category");
        return await Session.Put(new Category { Id = id, Name = name }, Ct) ? id : null;
    }

    List<ScopeItem> YieldScopes(RuleSet rs)
    {
        var grouped = new Dictionary<string, List<YieldRule>>(StringComparer.Ordinal);
        foreach (var y in rs.YieldRules.Values)
        {
            var key = string.IsNullOrEmpty(y.IngredientId) ? "c:" + (y.CategoryId ?? "") : "i:" + y.IngredientId;
            if (!grouped.TryGetValue(key, out var list)) grouped[key] = list = [];
            list.Add(y);
        }
        var scopes = new List<ScopeItem>(grouped.Count);
        foreach (var (key, rules) in grouped)
        {
            var ingredient = key[0] == 'i';
            var id = key[2..];
            var label = ingredient ? Session.IngredientName(id) : Session.CategoryName(id);
            if (label == "") label = "(ohne Zuordnung)";
            var items = rules.OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => new YieldItem(r)).ToList();
            var search = label + " " + string.Join(" ", rules.Select(r => r.Name));
            scopes.Add(new ScopeItem(id, ingredient, label, search, items));
        }
        return scopes.OrderBy(s => s.Ingredient).ThenBy(s => s.Label, StringComparer.Ordinal).ToList();
    }

    void ScopeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!loading && ScopeGrid.SelectedItem is ScopeItem item) ShowScope(item, null);
    }

    void ShowScope(ScopeItem scope, string? preferId)
    {
        var f = model.Yields;
        f.ScopeTitle = scope.Label;
        f.Rules.Clear();
        foreach (var r in scope.Rules) f.Rules.Add(r);
        var pick = f.Rules.FirstOrDefault(r => r.Rule.Id == preferId)
                   ?? f.Rules.FirstOrDefault(r => r.Rule.Default)
                   ?? f.Rules.FirstOrDefault();
        loading = true;
        RuleGrid.SelectedItem = pick;
        loading = false;
        if (pick is not null) LoadYield(pick.Rule); else f.Active = false;
    }

    void YieldSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!loading && RuleGrid.SelectedItem is YieldItem item) LoadYield(item.Rule);
    }

    void LoadYield(YieldRule y)
    {
        var f = model.Yields;
        f.CurrentId = y.Id;
        f.Existing = f.Active = true;
        f.Title = y.Name;
        f.Name = y.Name;
        f.IsDefault = y.Default;
        f.ScopeIndex = string.IsNullOrEmpty(y.IngredientId) ? 0 : 1;
        f.Category.Load(Session.Categories(), y.CategoryId);
        f.Ingredient = f.IngredientOptions.Find(i => i.Id == (y.IngredientId ?? "")) ?? YieldForm.None;
        f.Shrinkage = Input.BpText(y.Shrinkage);
        f.OwnUse = Input.BpText(y.OwnUse);
        f.Staff = Input.BpText(y.Staff);
        f.Free = Input.BpText(y.Free);
    }

    void NewYield(object? sender, RoutedEventArgs e)
    {
        var f = model.Yields;
        var scope = ScopeGrid.SelectedItem as ScopeItem;
        loading = true;
        RuleGrid.SelectedItem = null;
        loading = false;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neue Ertragsregel";
        f.Name = f.Shrinkage = f.OwnUse = f.Staff = f.Free = "";
        f.IsDefault = false;
        // A new rule starts in the scope that is open — almost always the one meant.
        f.ScopeIndex = scope is { Ingredient: true } ? 1 : 0;
        f.Category.Load(Session.Categories(), scope is { Ingredient: false } ? scope.Id : null);
        f.Ingredient = scope is { Ingredient: true }
            ? f.IngredientOptions.Find(i => i.Id == scope.Id) ?? YieldForm.None
            : YieldForm.None;
    }

    async void SaveYield(object? sender, RoutedEventArgs e)
    {
        var f = model.Yields;
        var id = f.CurrentId ?? Session.NewId("yield_rule");
        var rates = new[] { Input.Bp(f.Shrinkage), Input.Bp(f.OwnUse), Input.Bp(f.Staff), Input.Bp(f.Free) };
        var ingredientId = f.ScopeIsIngredient && f.Ingredient is { Id: not "" } ing ? ing.Id : null;
        f.NameInvalid = f.Name.Trim() == "";
        f.ScopeInvalid = f.ScopeIsIngredient
            ? ingredientId is null
            : !f.Category.Creating && f.Category.Selected?.Id is null or "";
        f.ShrinkageInvalid = rates[0] is not >= 0;
        f.OwnUseInvalid = rates[1] is not >= 0;
        f.StaffInvalid = rates[2] is not >= 0;
        f.FreeInvalid = rates[3] is not >= 0;
        var rated = rates.All(r => r is >= 0);
        var over = rated && rates.Sum(r => r!.Value) > Bp.Full;
        if (over) f.MarkRates(true);
        if (f.NameInvalid || f.ScopeInvalid || !rated || over)
        {
            Session.Fail(Missing(
                f.NameInvalid ? "Name" : null,
                f.ScopeInvalid ? f.ScopeKind : null,
                rated ? null : "Anteile (Prozentwerte, nicht negativ)",
                over ? "Anteile (zusammen höchstens 100 %)" : null));
            return;
        }
        var data = new YieldRule
        {
            Id = id,
            Name = f.Name.Trim(),
            Default = f.IsDefault,
            IngredientId = ingredientId,
            Shrinkage = rates[0]!.Value,
            OwnUse = rates[1]!.Value,
            Staff = rates[2]!.Value,
            Free = rates[3]!.Value,
        };
        await Compose(async () =>
        {
            if (!f.ScopeIsIngredient)
            {
                if (await CategoryId(f.Category) is not { } categoryId) return;
                data.CategoryId = categoryId == "" ? null : categoryId;
            }
            f.CurrentId = id;
            await Session.Put(data, Ct);
        });
    }

    async void DeleteYield(object? sender, RoutedEventArgs e) => await Delete(model.Yields, Entity.YieldRule);
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
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

public sealed class GewerbeItem(Gewerbezweig zweig)
{
    public Gewerbezweig Zweig { get; } = zweig;
    public string Kennzahl => Zweig.Kennzahl;
    public string Name => Zweig.Name;
    public string Search => Kennzahl + " " + Name;
}

// A row of the left column: the category (or ingredient) whose rules are the alternatives a Prüfung picks from.
public sealed class ScopeItem(string id, bool ingredient, string label, List<YieldRule> rules)
{
    public string Id { get; } = id;
    public bool Ingredient { get; } = ingredient;
    public string Label { get; } = label;
    public List<YieldRule> Rules { get; } = rules;
    public string Search => Label + " " + string.Join(" ", Rules.Select(r => r.Name));
    public string Hint => (Ingredient ? "Zutat" : "") + (Ingredient && Rules.Count > 0 ? " · " : "") + (Rules.Count > 0 ? Rules.Count.ToString() : "");
}

// One rule of the open scope; the last row has no id yet and becomes a rule once named and rated.
public sealed class YieldRow : Observable
{
    string? id;
    string name = "", deduction = "";
    bool isDefault, nameInvalid, deductionInvalid;

    public YieldRow(Func<YieldRow, Task> save) => Save = new Autosave(() => save(this));

    public Autosave Save { get; }
    public string? Id { get => id; set { if (Set(ref id, value)) { Raise(nameof(Existing)); Raise(nameof(Placeholder)); } } }
    public bool Existing => id is not null;
    public string Placeholder => id is null ? "Neue Regel …" : "";
    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Deduction { get => deduction; set { if (Set(ref deduction, value)) DeductionInvalid = false; } }
    public bool IsDefault { get => isDefault; set => Set(ref isDefault, value); }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public bool DeductionInvalid { get => deductionInvalid; set => Set(ref deductionInvalid, value); }
    public bool Blank => name.Trim() == "" && deduction.Trim() == "";

    public void Load(YieldRule r)
    {
        Id = r.Id;
        Name = r.Name;
        Deduction = Input.BpText(r.Deduction);
        IsDefault = r.Default;
    }
}

// A line is an ingredient or, as a sub-recipe, a product of the catalog counted in portions.
public sealed class RecipeRow(List<Ingredient> options, List<Product> products) : Observable
{
    public static readonly string[] Kinds = ["Zutat", "Produkt"];

    Ingredient? ingredient;
    Product? part;
    string amount = "";
    int unitIndex;
    bool isPart, ingredientInvalid, amountInvalid;

    public List<Ingredient> Options { get; } = options;
    public List<Product> Products { get; } = products;
    public List<string> Units { get; } = [.. RulesView.RecipeUnits.Select(c => Model.Units.Label(c))];
    public Ingredient? Ingredient
    {
        get => ingredient;
        set { if (Set(ref ingredient, value)) IngredientInvalid = false; }
    }
    public Product? Part
    {
        get => part;
        set { if (Set(ref part, value)) IngredientInvalid = false; }
    }
    public bool IsPart
    {
        get => isPart;
        set
        {
            if (!Set(ref isPart, value)) return;
            IngredientInvalid = false;
            if (value) UnitIndex = Array.IndexOf(RulesView.RecipeUnits, RulesView.PortionUnit);
            Raise(nameof(IsIngredient));
            Raise(nameof(KindIndex));
        }
    }
    public bool IsIngredient => !isPart;
    public int KindIndex { get => isPart ? 1 : 0; set => IsPart = value == 1; }
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
    string name = "", origin = "";
    bool nameInvalid;

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Origin { get => origin; set => Set(ref origin, value); }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public ObservableCollection<RecipeRow> Recipe { get; } = [];
}

public sealed class GewerbeForm : EntityForm<GewerbeItem>
{
    string kennzahl = "", name = "";
    bool kennzahlInvalid, nameInvalid;

    public string Kennzahl { get => kennzahl; set { if (Set(ref kennzahl, value)) KennzahlInvalid = false; } }
    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public bool KennzahlInvalid { get => kennzahlInvalid; set => Set(ref kennzahlInvalid, value); }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
}

public sealed class YieldForm : EntityForm<ScopeItem>
{
    public ObservableCollection<YieldRow> Rules { get; } = [];
    public ScopeItem? Scope { get; set; }
}

public sealed class RulesModel
{
    public IngredientForm Ingredients { get; } = new();
    public ProductForm Products { get; } = new();
    public YieldForm Yields { get; } = new();
    public GewerbeForm Gewerbe { get; } = new();
}

public partial class RulesView : Screen
{
    public static readonly string[] RecipeUnits = ["GRM", "KGM", "MLT", "LTR", "H87"];
    public const string PortionUnit = "H87";

    const int IngredientPage = 0, GewerbePage = 3;

    readonly RulesModel model = new();
    readonly Autosave ingredientSave, gewerbeSave;
    bool loading, saving, filling;
    Action<string>? productCreated;
    (string Id, List<RecipeLine>? Recipe)? wanted;

    public override string Topic => Tabs.SelectedIndex switch
    {
        1 => Help.Rules + "#produkte",
        2 => Help.Rules + "#ertragsregeln",
        3 => Help.Rules + "#gewerbe",
        _ => Help.Rules + "#zutaten",
    };

    protected override History History => Session.RulesHistory;

    protected override int Page => Tabs.SelectedIndex;

    // Products still wait for their save button, so typing there is undone in the field.
    public bool TypingFirst => Tabs.SelectedIndex is 1;

    public RulesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        IngredientSearch.Attach(model.Ingredients.Items, i => i.Search);
        ProductSearch.Attach(model.Products.Items, p => p.Name + " " + p.Recipe);
        YieldSearch.Attach(model.Yields.Items, s => s.Search);
        GewerbeSearch.Attach(model.Gewerbe.Items, g => g.Search);
        GewerbeGrid.ItemsSource = GewerbeSearch.View;
        IngredientGrid.ItemsSource = IngredientSearch.View;
        ProductGrid.ItemsSource = ProductSearch.View;
        ScopeGrid.ItemsSource = YieldSearch.View;
        ingredientSave = new Autosave(SaveIngredient);
        gewerbeSave = new Autosave(SaveGewerbe);
        model.Ingredients.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(IngredientForm.Name) or nameof(IngredientForm.Aliases)
                or nameof(IngredientForm.Piece) or nameof(IngredientForm.PieceUnitIndex)) Edited(ingredientSave);
        };
        // A new category is saved once its name is complete, not letter by letter.
        model.Ingredients.Category.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CategoryPicker.Selected) && !model.Ingredients.Category.Creating) Edited(ingredientSave);
        };
        model.Gewerbe.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GewerbeForm.Kennzahl) or nameof(GewerbeForm.Name)) Edited(gewerbeSave);
        };
    }

    Task SaveYieldRows() => Task.WhenAll(model.Yields.Rules.ToList().Select(r => r.Save.Now()));

    void Edited(Autosave save)
    {
        if (!filling) save.Schedule();
    }

    protected override async void OnEnter() => await LoadRules();

    protected override void Render(RuleSet rules) => Rebuild();

    protected override void OnLeave()
    {
        _ = ingredientSave.Now();
        _ = gewerbeSave.Now();
        _ = SaveYieldRows();
    }

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

        var scopes = YieldScopes(rs, ingredients);
        model.Yields.Items.Clear();
        foreach (var scope in scopes) model.Yields.Items.Add(scope);
        var open = model.Yields.Scope;
        ScopeGrid.SelectedItem = scopes.Find(s => s.Rules.Exists(r => r.Id == model.Yields.CurrentId))
            ?? scopes.Find(s => open is not null && s.Id == open.Id && s.Ingredient == open.Ingredient)
            ?? scopes.FirstOrDefault();

        model.Gewerbe.Items.Clear();
        foreach (var g in Session.Gewerbezweige()) model.Gewerbe.Items.Add(new GewerbeItem(g));
        GewerbeGrid.SelectedItem = model.Gewerbe.Items.FirstOrDefault(g => g.Zweig.Id == model.Gewerbe.CurrentId);

        loading = false;
        if (!gewerbeSave.Busy)
        {
            if (GewerbeGrid.SelectedItem is GewerbeItem gi) LoadGewerbe(gi.Zweig);
            else if (model.Gewerbe.CurrentId is not null) model.Gewerbe.Active = false;
        }
        if (!ingredientSave.Busy)
        {
            if (IngredientGrid.SelectedItem is IngredientItem ii) LoadIngredient(ii.Ingredient);
            else model.Ingredients.Active = false;
        }
        if (ProductGrid.SelectedItem is ProductItem pi) LoadProduct(pi.Product);
        else if (model.Products.CurrentId is not null) model.Products.Active = false;
        if (ScopeGrid.SelectedItem is ScopeItem si) ShowScope(si); else model.Yields.Active = false;
        if (wanted is { } w) EditProduct(w.Id, w.Recipe);
        wanted = null;
    }

    // The rules are rebuilt from the store once the step is written; the tab it was made on shows its entry.
    public async Task Move(bool back)
    {
        if (!IsActive) return;
        await ingredientSave.Now();
        await gewerbeSave.Now();
        await SaveYieldRows();
        if ((back ? await History.Undo() : await History.Redo()) is not { } place) return;
        Tabs.SelectedIndex = place.Page;
        EntityForm form = place.Page switch
        {
            1 => model.Products,
            2 => model.Yields,
            3 => model.Gewerbe,
            _ => model.Ingredients,
        };
        form.CurrentId = place.Item;
        Rebuild();
        if (form.CurrentId != place.Item) return;
        var (list, item) = place.Page switch
        {
            1 => ((Control)ProductGrid, ProductGrid.SelectedItem),
            2 => (RuleList, model.Yields.Rules.FirstOrDefault(r => r.Id == place.Item)),
            3 => (GewerbeGrid, GewerbeGrid.SelectedItem),
            _ => (IngredientGrid, IngredientGrid.SelectedItem),
        };
        Reveal.Row(list, item);
    }

    static string Missing(params string?[] fields) =>
        "Bitte prüfen: " + string.Join(", ", fields.OfType<string>()) + ".";

    async Task Delete(EntityForm form, Entity entity)
    {
        if (form.CurrentId is not { } id || !await Confirmed(form.Title, entity)) return;
        if (form == model.Ingredients) ingredientSave.Cancel();
        if (form == model.Gewerbe) gewerbeSave.Cancel();
        if (await Session.Delete(entity, id, At(id), Ct)) form.CurrentId = null;
    }

    Task<bool> Confirmed(string title, Entity entity) =>
        Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "„" + title + "“ wird dauerhaft aus den Regeln entfernt. Bereits erstellte Berichte bleiben unverändert.",
            Format.EntityName(entity) + " löschen");

    void IngredientSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (loading || IngredientGrid.SelectedItem is not IngredientItem item) return;
        _ = ingredientSave.Now();
        LoadIngredient(item.Ingredient);
    }

    void LoadIngredient(Ingredient i)
    {
        var f = model.Ingredients;
        filling = true;
        f.CurrentId = i.Id;
        f.Existing = f.Active = true;
        f.Title = i.Name;
        f.Name = i.Name;
        f.Aliases = string.Join(Environment.NewLine, i.Aliases);
        f.LoadPiece(i.Piece, Session.Rules is { } rs ? Scale.Of(rs, i.Id) : null);
        f.Category.Load(Session.Categories(), i.CategoryId);
        filling = false;
    }

    void NewIngredient(object? sender, RoutedEventArgs e)
    {
        _ = ingredientSave.Now();
        var f = model.Ingredients;
        filling = true;
        IngredientGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neue Zutat";
        f.Name = f.Aliases = "";
        f.LoadPiece(null, null);
        f.Category.Load(Session.Categories(), null);
        filling = false;
    }

    void CategoryNamed(object? sender, RoutedEventArgs e)
    {
        if (!model.Ingredients.Category.Creating) return;
        ingredientSave.Schedule();
        _ = ingredientSave.Now();
    }

    void CategoryNameKey(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) CategoryNamed(sender, e);
    }

    // The form is read before the first await, so a save started by picking another entry takes the one left.
    async Task SaveIngredient()
    {
        var f = model.Ingredients;
        var weight = Input.Int(f.Piece);
        f.NameInvalid = f.Name.Trim() == "";
        f.PieceInvalid = f.Piece.Trim() != "" && weight is not > 0;
        f.Category.Invalid = f.Category.Creating && f.Category.NewName.Trim() == "";
        if (f.NameInvalid || f.PieceInvalid || f.Category.Invalid) return;
        var id = f.CurrentId ?? Session.NewId("ingredient");
        var at = new Place(History, IngredientPage, id);
        var data = new Ingredient
        {
            Id = id, Name = f.Name.Trim(), Aliases = AliasLines(f.Aliases),
            Piece = weight is { } w ? new Piece(w, IngredientForm.PieceUnits[f.PieceUnitIndex]) : null,
        };
        f.CurrentId = id;
        if (await CategoryId(f.Category, at) is not { } categoryId) return;
        data.CategoryId = categoryId;
        if (!await Session.Put(data, at, CancellationToken.None) || f.CurrentId != id) return;
        filling = true;
        f.Existing = true;
        f.Title = data.Name;
        if (f.Category.Creating) f.Category.Load(Session.Categories(), categoryId);
        filling = false;
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
        var parts = Parts(p.Id);
        f.CurrentId = p.Id;
        f.Existing = f.Active = true;
        f.Title = p.Name;
        f.Name = p.Name;
        f.Origin = "";
        f.Recipe.Clear();
        foreach (var l in p.Recipe) f.Recipe.Add(Row(options, parts, l));
    }

    // A product cannot be its own part; deeper loops the rule check refuses on save.
    List<Product> Parts(string? productId) => [.. Session.Products().Where(p => p.Id != productId)];

    static RecipeRow Row(List<Ingredient> options, List<Product> parts, RecipeLine l) => new(options, parts)
    {
        IsPart = l.ProductId is not null,
        Ingredient = options.Find(i => i.Id == l.IngredientId),
        Part = parts.Find(p => p.Id == l.ProductId),
        Amount = l.Amount.ToString(),
        UnitIndex = Math.Max(Array.IndexOf(RecipeUnits, l.Unit), 0),
    };

    // Before the first rule set arrives the list is empty; Rebuild comes back here.
    public void EditProduct(string id, List<RecipeLine>? recipe)
    {
        Tabs.SelectedIndex = 1;
        ProductSearch.Reset();
        if (model.Products.Items.FirstOrDefault(p => p.Product.Id == id) is not { } item)
        {
            wanted = (id, recipe);
            return;
        }
        loading = true;
        ProductGrid.SelectedItem = item;
        loading = false;
        ProductGrid.ScrollIntoView(item);
        LoadProduct(item.Product);
        if (recipe is null) return;
        var options = Session.Ingredients();
        var parts = Parts(id);
        model.Products.Recipe.Clear();
        foreach (var l in recipe) model.Products.Recipe.Add(Row(options, parts, l));
        model.Products.Origin = "Rezeptur aus der Prüfung übernommen. Erst mit Speichern gilt sie im Katalog für alle Prüfungen.";
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
        f.Origin = "";
        f.Recipe.Clear();
        if (created is not null) f.Recipe.Add(new RecipeRow(Session.Ingredients(), Parts(null)));
    }

    void AddRecipeLine(object? sender, RoutedEventArgs e) =>
        model.Products.Recipe.Add(new RecipeRow(Session.Ingredients(), Parts(model.Products.CurrentId)));

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
            row.IngredientInvalid = row.IsPart ? row.Part is null : row.Ingredient is null;
            row.AmountInvalid = amount is not > 0;
            if (row.IngredientInvalid || row.AmountInvalid) lines = false;
            else if (row.IsPart) data.Recipe.Add(new RecipeLine { ProductId = row.Part!.Id, Amount = amount!.Value, Unit = PortionUnit });
            else data.Recipe.Add(new RecipeLine { IngredientId = row.Ingredient!.Id, Amount = amount!.Value, Unit = RecipeUnits[row.UnitIndex] });
        }
        if (f.NameInvalid || !lines || data.Recipe.Count == 0)
        {
            Session.Fail(Missing(
                f.NameInvalid ? "Name" : null,
                f.Recipe.Count == 0 ? "Rezept (mindestens eine Zutat)"
                    : lines ? null : "Rezept (Zutat oder Produkt und Menge je Portion, Menge größer als 0)"));
            return;
        }
        var isNew = f.CurrentId is null;
        f.CurrentId = id;
        if (await Session.Put(data, At(id), Ct) && isNew && productCreated is { } created)
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
    async Task<string?> CategoryId(CategoryPicker picker, Place at)
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
        return await Session.Put(new Category { Id = id, Name = name }, at, CancellationToken.None) ? id : null;
    }

    List<ScopeItem> YieldScopes(RuleSet rs, List<Ingredient> ingredients)
    {
        var byCategory = new Dictionary<string, List<YieldRule>>(StringComparer.Ordinal);
        var byIngredient = new Dictionary<string, List<YieldRule>>(StringComparer.Ordinal);
        foreach (var y in rs.YieldRules.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Deduction))
        {
            var (map, key) = string.IsNullOrEmpty(y.IngredientId) ? (byCategory, y.CategoryId ?? "") : (byIngredient, y.IngredientId);
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(y);
        }
        var categories = Session.Categories().Select(c => new ScopeItem(c.Id, false, c.Name, byCategory.GetValueOrDefault(c.Id, [])));
        var items = ingredients.Select(i => new ScopeItem(i.Id, true, i.Name, byIngredient.GetValueOrDefault(i.Id, [])));
        return [.. categories.OrderBy(s => s.Label, StringComparer.Ordinal), .. items.OrderBy(s => s.Label, StringComparer.Ordinal)];
    }

    void ScopeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (loading || ScopeGrid.SelectedItem is not ScopeItem item) return;
        _ = SaveYieldRows();
        ShowScope(item);
    }

    // Rows are kept, not rebuilt, so a save landing mid-typing leaves the caret where it is.
    void ShowScope(ScopeItem scope)
    {
        var f = model.Yields;
        var same = f.Scope is { } open && open.Id == scope.Id && open.Ingredient == scope.Ingredient;
        f.Scope = scope;
        f.Active = true;
        f.Title = scope.Label;
        if (!same) f.Rules.Clear();
        filling = true;
        foreach (var row in f.Rules.ToList())
        {
            if (row.Save.Busy || row.Id is null) continue;
            if (scope.Rules.Find(r => r.Id == row.Id) is { } rule) row.Load(rule);
            else f.Rules.Remove(row);
        }
        foreach (var rule in scope.Rules)
        {
            if (f.Rules.Any(r => r.Id == rule.Id)) continue;
            var row = NewYieldRow();
            row.Load(rule);
            f.Rules.Insert(f.Rules.TakeWhile(r => r.Id is not null).Count(), row);
        }
        if (!f.Rules.Any(r => r.Id is null)) f.Rules.Add(NewYieldRow());
        filling = false;
    }

    YieldRow NewYieldRow()
    {
        var row = new YieldRow(SaveYield);
        row.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(YieldRow.Name) or nameof(YieldRow.Deduction)) Edited(row.Save);
            if (e.PropertyName is nameof(YieldRow.IsDefault) && !filling)
            {
                row.Save.Schedule();
                _ = row.Save.Now();
            }
        };
        return row;
    }

    void YieldRowLeft(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is YieldRow row) _ = row.Save.Now();
    }

    // A new row waits quietly until it has both a name and a rate; an existing one says what is wrong.
    async Task SaveYield(YieldRow row)
    {
        if (model.Yields.Scope is not { } scope) return;
        var name = row.Name.Trim();
        var deduction = Input.Bp(row.Deduction);
        var rated = deduction is >= 0 and <= Bp.Full;
        if (row.Id is null && (name == "" || row.Deduction.Trim() == "")) return;
        row.NameInvalid = name == "";
        row.DeductionInvalid = !rated;
        if (row.NameInvalid || row.DeductionInvalid) return;
        var isNew = row.Id is null;
        var id = row.Id ?? Session.NewId("yield_rule");
        var data = new YieldRule
        {
            Id = id,
            Name = name,
            CategoryId = scope.Ingredient ? null : scope.Id,
            IngredientId = scope.Ingredient ? scope.Id : null,
            Deduction = deduction!.Value,
            Default = row.IsDefault,
        };
        row.Id = id;
        model.Yields.CurrentId = id;
        if (isNew) model.Yields.Rules.Add(NewYieldRow());
        // Only one rule of a scope is its default; the one it replaces is cleared in the same step.
        var at = At(id);
        var cleared = true;
        if (data.Default && Session.Rules is { } rs)
            foreach (var other in rs.YieldRules.Values.Where(r => r.Default && r.Id != id && r.IngredientId == data.IngredientId && r.CategoryId == data.CategoryId).ToList())
            {
                var off = Json.Copy(other);
                off.Default = false;
                cleared &= await Session.Put(off, at, CancellationToken.None);
            }
        if (cleared && await Session.Put(data, at, CancellationToken.None) || !isNew) return;
        row.Id = null;
        if (model.Yields.Rules.LastOrDefault() is { Id: null, Blank: true } extra && extra != row) model.Yields.Rules.Remove(extra);
    }

    async void DeleteYield(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not YieldRow { Id: { } id } row) return;
        if (!await Confirmed(row.Name, Entity.YieldRule)) return;
        row.Save.Cancel();
        await Session.Delete(Entity.YieldRule, id, At(id), Ct);
    }

    void GewerbeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (loading || GewerbeGrid.SelectedItem is not GewerbeItem item) return;
        _ = gewerbeSave.Now();
        LoadGewerbe(item.Zweig);
    }

    void LoadGewerbe(Gewerbezweig g)
    {
        var f = model.Gewerbe;
        filling = true;
        f.CurrentId = g.Id;
        f.Existing = f.Active = true;
        f.Title = g.Kennzahl + " " + g.Name;
        f.Kennzahl = g.Kennzahl;
        f.Name = g.Name;
        filling = false;
    }

    void NewGewerbe(object? sender, RoutedEventArgs e)
    {
        _ = gewerbeSave.Now();
        var f = model.Gewerbe;
        filling = true;
        GewerbeGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neue Gewerbekennzahl";
        f.Kennzahl = f.Name = "";
        filling = false;
    }

    async Task SaveGewerbe()
    {
        var f = model.Gewerbe;
        var data = new Gewerbezweig { Id = f.CurrentId ?? Session.NewId("gewerbe"), Kennzahl = f.Kennzahl.Trim(), Name = f.Name.Trim() };
        var taken = Session.Gewerbezweige().Exists(g => g.Kennzahl == data.Kennzahl && g.Id != data.Id);
        f.KennzahlInvalid = !Gewerbe.Kennzahl(data.Kennzahl) || taken;
        f.NameInvalid = data.Name == "";
        if (f.KennzahlInvalid || f.NameInvalid) return;
        f.CurrentId = data.Id;
        if (!await Session.Put(data, new Place(History, GewerbePage, data.Id), CancellationToken.None) || f.CurrentId != data.Id) return;
        f.Existing = true;
        f.Title = data.Kennzahl + " " + data.Name;
    }

    async void DeleteGewerbe(object? sender, RoutedEventArgs e) => await Delete(model.Gewerbe, Entity.Gewerbezweig);
}

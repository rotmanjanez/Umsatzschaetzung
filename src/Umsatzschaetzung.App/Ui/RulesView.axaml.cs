using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;

namespace Umsatzschaetzung.App.Ui;

public sealed class ProductItem(Product product, string category, string recipe)
{
    public Product Product { get; } = product;
    public string Name => Product.Name;
    public string Category { get; } = category;
    public bool HasCategory => Category != "";
    public string Aliases => string.Join(", ", Product.Aliases);
    public string Kind => Units.Label(Product.Unit) + (Product.Recipe.Count > 0 ? " · Rezept" : "");
    public string Search => Name + " " + Kind + " " + Category + " " + Aliases;
    public string Spoken => string.Join(", ", new[] { Name, Kind, Category }.Where(t => t != ""));
    public string Tip => string.Join("\n", new[] { Name, Category, Aliases, recipe == "" ? "" : "Rezept: " + recipe }.Where(t => t != ""));
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

public sealed class GewerbeItem(Gewerbezweig zweig)
{
    public Gewerbezweig Zweig { get; } = zweig;
    public string Kennzahl => Zweig.Kennzahl;
    public string Name => Zweig.Name;
    public string Search => Kennzahl + " " + Name;
}

public sealed class TemplateItem(ReportTemplate template)
{
    public ReportTemplate Template { get; } = template;
    public string Label => Template.Default ? Template.Name + " (Standard)" : Template.Name;
}

// A row of the left column: the category (or product) whose rules are the alternatives a Prüfung picks from.
public sealed class ScopeItem(string id, bool product, string label, List<YieldRule> rules)
{
    public string Id { get; } = id;
    public bool Product { get; } = product;
    public string Label { get; } = label;
    public List<YieldRule> Rules { get; } = rules;
    public string Search => Label + " " + string.Join(" ", Rules.Select(r => r.Name));
    public string Hint => (Product ? "Produkt" : "") + (Product && Rules.Count > 0 ? " · " : "") + (Rules.Count > 0 ? Rules.Count.ToString() : "");
    public string Spoken => Label + (Product ? ", Produkt, " : ", Kategorie, ") + Rules.Count switch
    {
        0 => "keine Regel",
        1 => "1 Regel",
        var n => n + " Regeln",
    };
}

// One rule of the open scope; the last row has no id yet and becomes a rule once named and rated.
public sealed class YieldRow : Observable
{
    string? id;
    string name = "", deduction = "";
    bool isDefault, nameInvalid, deductionInvalid;

    public YieldRow(Func<YieldRow, Task<bool>> save) => Save = new Autosave(_ => save(this));

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

public sealed class RecipeRow(List<Product> options, PartLine? line) : PartRow(options, line?.PartId, line?.Unit)
{
    string amount = line?.Amount.ToString() ?? "";
    bool partInvalid, amountInvalid;

    public string Amount { get => amount; set { if (Set(ref amount, value)) AmountInvalid = false; } }
    public bool PartInvalid { get => partInvalid; set => Set(ref partInvalid, value); }
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
    public Rows<T> Items { get; } = [];
}

public sealed class ProductForm : EntityForm<ProductItem>
{
    public static readonly string[] Counted = ["GRM", "MLT", "H87"];
    public static readonly Unit[] PieceUnits = [Unit.G, Unit.Ml];

    string name = "", aliases = "", piece = "", batch = "1", origin = "";
    List<string> unitCodes = [.. Counted];
    int unitIndex, pieceUnitIndex;
    bool nameInvalid, pieceInvalid, batchInvalid, unitFree = true, pieceUnitFree = true;

    public ProductForm() => Recipe.CollectionChanged += (_, _) => Raise(nameof(HasRecipe));

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Aliases { get => aliases; set => Set(ref aliases, value); }
    public CategoryPicker Category { get; } = new();
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
    public List<string> UnitNames => [.. unitCodes.Select(Units.Label)];
    public int UnitIndex
    {
        get => unitIndex;
        set
        {
            if (value < 0 || !Set(ref unitIndex, value)) return;
            Raise(nameof(BatchUnit));
            FixPieceUnit();
        }
    }
    public string UnitCode => unitCodes[unitIndex];
    // Recipes and mappings count in the unit; while one does, it stays.
    public bool UnitFree { get => unitFree; set { if (Set(ref unitFree, value)) Raise(nameof(UnitLocked)); } }
    public bool UnitLocked => !unitFree;
    public string Batch { get => batch; set { if (Set(ref batch, value)) BatchInvalid = false; } }
    public string BatchUnit => Format.UnitName(Units.Lookup(UnitCode)?.Base ?? Unit.Piece);
    public bool BatchInvalid { get => batchInvalid; set => Set(ref batchInvalid, value); }
    public string Piece { get => piece; set { if (Set(ref piece, value)) PieceInvalid = false; } }
    public List<string> PieceUnitNames { get; } = [.. PieceUnits.Select(Format.UnitName)];
    public int PieceUnitIndex { get => pieceUnitIndex; set => Set(ref pieceUnitIndex, value); }
    // A product counted in g or ml weighs its pieces in that; only one counted in pieces may choose.
    public bool PieceUnitFree { get => pieceUnitFree; set => Set(ref pieceUnitFree, value); }
    public bool PieceInvalid { get => pieceInvalid; set => Set(ref pieceInvalid, value); }
    public string Origin { get => origin; set => Set(ref origin, value); }
    public ObservableCollection<RecipeRow> Recipe { get; } = [];
    public bool HasRecipe => Recipe.Count > 0;

    // The product the form was filled from; a field reading as it did there is one the user left alone.
    public Product? Loaded { get; private set; }

    public bool NameEdited => Loaded is not { } p || Name != p.Name;
    public bool AliasesEdited => Loaded is not { } p || Aliases != AliasText(p);
    public bool UnitEdited => Loaded is not { } p || UnitCode != p.Unit;
    public bool BatchEdited => Loaded is not { } p || Batch != Format.Group(p.Batch);
    public bool PieceEdited => Loaded is not { } p || Piece != PieceText(p) || Piece != "" && PieceUnitIndex != PieceIndex(p);

    static string AliasText(Product p) => string.Join(Environment.NewLine, p.Aliases);
    static string PieceText(Product p) => p.Piece is { } w ? Format.Group(w.Amount) : "";
    static int PieceIndex(Product p) => Math.Max(Array.IndexOf(PieceUnits, p.Piece?.Unit ?? Unit.G), 0);

    public void Load(Product? p, bool unitFree)
    {
        Loaded = null;
        Name = p?.Name ?? "";
        Aliases = p is null ? "" : AliasText(p);
        SetUnit(p?.Unit ?? "H87");
        UnitFree = unitFree;
        Batch = Format.Group(p?.Batch ?? 1);
        Piece = p is null ? "" : PieceText(p);
        PieceUnitIndex = p is null ? 0 : PieceIndex(p);
        FixPieceUnit();
        Origin = "";
        Recipe.Clear();
        Loaded = p;
    }

    // The stored product moved on while the form was being edited: what the user left alone follows it.
    public void Rebase(Product now)
    {
        if (!NameEdited) Name = now.Name;
        if (!AliasesEdited) Aliases = AliasText(now);
        if (!UnitEdited) SetUnit(now.Unit);
        if (!BatchEdited) Batch = Format.Group(now.Batch);
        if (!PieceEdited)
        {
            Piece = PieceText(now);
            PieceUnitIndex = PieceIndex(now);
            FixPieceUnit();
        }
        Loaded = now;
    }

    // A unit the user picked that can no longer change goes back to the stored one.
    public void Lock(Product now)
    {
        UnitFree = false;
        SetUnit(now.Unit);
    }

    void SetUnit(string unit)
    {
        unitCodes = Counted.Contains(unit) ? [.. Counted] : [.. Counted, unit];
        Raise(nameof(UnitNames));
        unitIndex = -1;
        UnitIndex = unitCodes.IndexOf(unit);
    }

    void FixPieceUnit()
    {
        var counted = Units.Lookup(UnitCode)?.Base;
        PieceUnitFree = counted is not (Unit.G or Unit.Ml);
        if (!PieceUnitFree) PieceUnitIndex = Array.IndexOf(PieceUnits, counted!.Value);
    }
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

public sealed class TemplateForm : EntityForm<TemplateItem>
{
    string name = "", source = "";
    bool isDefault, nameInvalid;

    public string Name { get => name; set { if (Set(ref name, value)) NameInvalid = false; } }
    public string Source { get => source; set => Set(ref source, value); }
    public bool IsDefault { get => isDefault; set => Set(ref isDefault, value); }
    public bool NameInvalid { get => nameInvalid; set => Set(ref nameInvalid, value); }
}

public sealed class YieldForm : EntityForm<ScopeItem>
{
    public ObservableCollection<YieldRow> Rules { get; } = [];
    public ScopeItem? Scope { get; set; }
}

public sealed class RulesModel
{
    public ProductForm Products { get; } = new();
    public YieldForm Yields { get; } = new();
    public GewerbeForm Gewerbe { get; } = new();
    public TemplateForm Templates { get; } = new();
}

public partial class RulesView : Screen
{
    public static readonly string[] RecipeUnits = ["GRM", "KGM", "MLT", "LTR", "H87"];
    public static readonly FuncValueConverter<bool, string, string?> Flag = new((on, text) => on ? text : null);

    readonly RulesModel model = new();
    readonly Autosave productSave, gewerbeSave, templateSave;
    bool loading, filling, productEdited;
    int saving, productEdits;
    // A new product, or a new category, is made from the name it had when its field was left, not while it is typed.
    string? nameLeft, categoryLeft;
    Action<string>? productCreated;
    (string Id, List<PartLine>? Recipe)? wanted;

    public override string Topic =>
        Tabs.SelectedItem == ProductsTab ? Help.Rules + "#produkte"
        : Tabs.SelectedItem == YieldsTab ? Help.Rules + "#ertragsregeln"
        : Tabs.SelectedItem == GewerbeTab ? Help.Rules + "#gewerbe"
        : Help.Rules;

    protected override History History => Session.RulesHistory;

    protected override int Page => Tabs.SelectedIndex;

    // The template's text is saved once the field is left, so typing there is undone in the field.
    public bool TypingFirst => TemplateSource.IsFocused;

    public RulesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        TemplateSource.AddHandler(KeyDownEvent, LeaveOnControlTab, RoutingStrategies.Tunnel);
        ProductSearch.Attach(model.Products.Items, p => p.Search);
        YieldSearch.Attach(model.Yields.Items, s => s.Search);
        GewerbeSearch.Attach(model.Gewerbe.Items, g => g.Search);
        GewerbeGrid.ItemsSource = GewerbeSearch.View;
        TemplateGrid.ItemsSource = model.Templates.Items;
        ProductGrid.ItemsSource = ProductSearch.View;
        ScopeGrid.ItemsSource = YieldSearch.View;
        productSave = new Autosave(done => SaveProduct(done));
        gewerbeSave = new Autosave(_ => SaveGewerbe());
        templateSave = new Autosave(SaveTemplate);
        model.Products.Changed += ProductEdited;
        model.Products.Category.Changed += ProductEdited;
        model.Products.Recipe.CollectionChanged += (_, e) =>
        {
            foreach (RecipeRow row in e.NewItems ?? Array.Empty<RecipeRow>()) row.Changed += ProductEdited;
            ProductEdited();
        };
        model.Gewerbe.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GewerbeForm.Kennzahl) or nameof(GewerbeForm.Name)) Edited(gewerbeSave);
        };
        model.Templates.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TemplateForm.Name) or nameof(TemplateForm.IsDefault)) Edited(templateSave);
            else if (e.PropertyName is nameof(TemplateForm.Source) && !filling) templateSave.Mark();
        };
    }

    int PageOf(TabItem tab) => Tabs.Items.IndexOf(tab);

    Place AtPage(TabItem tab, string id) => new(History, PageOf(tab), id);

    // A recipe taken over from a Prüfung waits for its button.
    void ProductEdited()
    {
        if (filling) return;
        productEdited = true;
        productEdits++;
        if (model.Products.Origin == "") productSave.Schedule();
        else productSave.Mark();
    }

    public void FocusPage() => Tabs.ContainerFromIndex(Tabs.SelectedIndex)?.Focus();

    void LeaveOnControlTab(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab || !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
        e.Handled = true;
        var direction = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NavigationDirection.Previous : NavigationDirection.Next;
        TopLevel.GetTopLevel(this)?.FocusManager?.TryMoveFocus(direction);
    }

    async Task<bool> SaveYieldRows() => (await Task.WhenAll(model.Yields.Rules.ToList().Select(r => r.Save.Now()))).All(s => s);

    void Edited(Autosave save)
    {
        if (!filling) save.Schedule();
    }

    protected override async void OnEnter() => await LoadRules();

    protected override void Render(RuleSet rules) => Rebuild();

    protected override void OnLeave()
    {
        _ = productSave.Now();
        _ = gewerbeSave.Now();
        _ = templateSave.Now();
        _ = SaveYieldRows();
    }

    // The window stays until what was typed is in the store, or the user lets it go.
    public async Task<bool> Closing()
    {
        var saved = await Task.WhenAll(productSave.Now(true), gewerbeSave.Now(true), templateSave.Now(true), SaveYieldRows());
        await Session.Saved;
        return saved.All(s => s)
            || await Dialog.Confirm(this, "Nicht alle Änderungen sind gespeichert. Trotzdem schließen und sie verwerfen?", "Regeln schließen");
    }

    void Rebuild()
    {
        if (saving > 0 || !IsActive || Session.Rules is null) return;
        var rs = Session.Rules;
        loading = true;
        var products = Session.Products();

        ProductBox.SetCategoryNames(this, Session.CategoryNames);
        ProductBox.SetSimilar(this, Session.SimilarProducts);

        model.Products.Items.Replace(products.Select(p => new ProductItem(p, Session.CategoryName(p.CategoryId), Names.Recipe(rs, p))));
        ProductGrid.SelectedItem = model.Products.Items.FirstOrDefault(p => p.Product.Id == model.Products.CurrentId);

        var scopes = YieldScopes(rs, products);
        model.Yields.Items.Replace(scopes);
        var open = model.Yields.Scope;
        ScopeGrid.SelectedItem = scopes.Find(s => s.Rules.Exists(r => r.Id == model.Yields.CurrentId))
            ?? scopes.Find(s => open is not null && s.Id == open.Id && s.Product == open.Product)
            ?? scopes.FirstOrDefault();

        model.Gewerbe.Items.Replace(Session.Gewerbezweige().Select(g => new GewerbeItem(g)));
        GewerbeGrid.SelectedItem = model.Gewerbe.Items.FirstOrDefault(g => g.Zweig.Id == model.Gewerbe.CurrentId);

        model.Templates.Items.Replace(Templates(rs).Select(t => new TemplateItem(t)));
        TemplateGrid.SelectedItem = model.Templates.Items.FirstOrDefault(t => t.Template.Id == model.Templates.CurrentId);

        loading = false;
        if (!gewerbeSave.Busy)
        {
            if (GewerbeGrid.SelectedItem is GewerbeItem gi) LoadGewerbe(gi.Zweig);
            else if (model.Gewerbe.CurrentId is not null) model.Gewerbe.Active = false;
        }
        if (ProductGrid.SelectedItem is ProductItem pi)
        {
            if (productEdited) Rebase(pi.Product);
            else LoadProduct(pi.Product);
        }
        else if (model.Products.CurrentId is not null) model.Products.Active = false;
        if (ScopeGrid.SelectedItem is ScopeItem si) ShowScope(si); else model.Yields.Active = false;
        if (!templateSave.Busy)
        {
            if (TemplateGrid.SelectedItem is TemplateItem ti) LoadTemplate(ti.Template);
            else if (model.Templates.CurrentId is not null) model.Templates.Active = false;
        }
        if (wanted is { } w) EditProduct(w.Id, w.Recipe);
        wanted = null;
    }

    // The rules are rebuilt from the store once the step is written; the tab it was made on shows its entry.
    public async Task Move(bool back)
    {
        if (!IsActive) return;
        await Task.WhenAll(productSave.Now(), gewerbeSave.Now(), templateSave.Now(), SaveYieldRows());
        if ((back ? await History.Undo() : await History.Redo()) is not { } place) return;
        Tabs.SelectedIndex = place.Page;
        var tab = Tabs.SelectedItem;
        EntityForm form = tab == YieldsTab ? model.Yields : tab == GewerbeTab ? model.Gewerbe : tab == TemplatesTab ? model.Templates : model.Products;
        form.CurrentId = place.Item;
        productEdited = false;
        Rebuild();
        if (form.CurrentId != place.Item) return;
        var (list, item) =
            tab == YieldsTab ? (RuleList, model.Yields.Rules.FirstOrDefault(r => r.Id == place.Item))
            : tab == GewerbeTab ? ((Control)GewerbeGrid, GewerbeGrid.SelectedItem)
            : tab == TemplatesTab ? (TemplateGrid, TemplateGrid.SelectedItem)
            : (ProductGrid, ProductGrid.SelectedItem);
        Reveal.Row(list, item);
    }

    static string Missing(params string?[] fields) =>
        "Bitte prüfen: " + string.Join(", ", fields.OfType<string>()) + ".";

    async Task Delete(EntityForm form, Entity entity, Button next)
    {
        if (form.CurrentId is not { } id) return;
        if (Session.Rules is { } rs && RuleCheck.Users(rs, entity, id) is { Count: > 0 } users)
        {
            var named = users.Count > 6 ? [.. users.Take(5), $"{users.Count - 5} weiteren"] : users;
            await Dialog.Alert(this,
                "„" + form.Title + "“ wird noch verwendet von " + string.Join(", ", named) + ". Erst dort entfernen, dann löschen.",
                Format.EntityName(entity) + " löschen");
            return;
        }
        if (!await Confirmed(form.Title, entity)) return;
        (form == model.Products ? productSave : form == model.Gewerbe ? gewerbeSave : templateSave).Cancel();
        if (!await Session.Delete(entity, id, At(id), Ct)) return;
        form.CurrentId = null;
        if (form == model.Products) productEdited = false;
        next.Focus();
    }

    Task<bool> Confirmed(string title, Entity entity) =>
        Dialog.Confirm(this,
            "„" + title + "“ wird dauerhaft aus den Regeln entfernt. Bereits erstellte Berichte bleiben unverändert.",
            Format.EntityName(entity) + " löschen");

    void ProductSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (loading || ProductGrid.SelectedItem is not ProductItem item) return;
        _ = productSave.Now();
        LoadProduct(item.Product);
    }

    void LoadProduct(Product p)
    {
        var f = model.Products;
        var rs = Session.Rules;
        productCreated = null;
        productSave.Cancel();
        filling = true;
        f.CurrentId = p.Id;
        f.Existing = f.Active = true;
        f.Title = p.Name;
        f.Load(p, UnitFree(p.Id));
        f.Category.Load(Session.Categories(), p.CategoryId);
        var parts = Parts(p.Id);
        foreach (var l in p.Recipe) f.Recipe.Add(new RecipeRow(parts, l));
        filling = false;
        productEdited = false;
    }

    // Keeps what the user typed and takes the rest from the store as it is now; rows that still read
    // as stored are kept too, so a save landing mid-typing leaves the caret where it is.
    void Rebase(Product p)
    {
        var f = model.Products;
        var category = CategoryEdited();
        var recipe = RecipeEdited();
        filling = true;
        f.Existing = true;
        f.Title = p.Name;
        f.Rebase(p);
        f.UnitFree = UnitFree(p.Id);
        if (!f.UnitFree && f.UnitCode != p.Unit) Relock(p);
        if (!category || f.Category.Creating && string.Equals(Session.CategoryName(p.CategoryId), f.Category.NewName.Trim(), StringComparison.OrdinalIgnoreCase))
            f.Category.Load(Session.Categories(), p.CategoryId);
        else if (!f.Category.Creating) f.Category.Load(Session.Categories(), f.Category.Selected?.Id);
        var parts = Parts(p.Id);
        if (recipe || SameRecipe(p))
            foreach (var row in f.Recipe) row.Offer(parts);
        else
        {
            f.Recipe.Clear();
            foreach (var l in p.Recipe) f.Recipe.Add(new RecipeRow(parts, l));
        }
        filling = false;
    }

    // The unit the user picked is set back, and why is said: what uses the product meanwhile counts in it.
    void Relock(Product stored)
    {
        var users = Session.Rules is { } rs ? RuleCheck.CountedIn(rs, stored.Id) : [];
        var was = filling;
        filling = true;
        model.Products.Lock(stored);
        filling = was;
        Session.Fail($"Die Einheit bleibt {Units.Label(stored.Unit)}, solange es verwendet wird von {string.Join(", ", users)}.");
    }

    bool UnitFree(string id) => Session.Rules is not { } rs || RuleCheck.CountedIn(rs, id).Count == 0;

    bool CategoryEdited() =>
        model.Products.Category.Creating || (model.Products.Category.Selected?.Id ?? "") != (model.Products.Loaded?.CategoryId ?? "");

    bool RecipeEdited() => model.Products.Loaded is not { } p || !SameRecipe(p);

    bool SameRecipe(Product p) => RecipeLines() is { } lines && Recipes.Same(lines, p.Recipe);

    // Null while a line is not complete.
    List<PartLine>? RecipeLines()
    {
        List<PartLine> lines = [];
        foreach (var row in model.Products.Recipe)
        {
            if (row.Part is null || Input.Int(row.Amount) is not { } amount || amount <= 0) return null;
            lines.Add(new PartLine { PartId = row.Part.Id, Amount = amount, Unit = row.UnitCode });
        }
        return lines;
    }

    // Neither the product itself nor one it is made into can be its part.
    List<Product> Parts(string? productId)
    {
        var loop = Session.Rules is { } rs ? PartRow.Containing(rs, productId) : [];
        return [.. Session.Products().Where(p => p.Id != productId && !loop.Contains(p.Id))];
    }

    // Before the first rule set arrives the list is empty; Rebuild comes back here.
    public void EditProduct(string id, List<PartLine>? recipe)
    {
        Tabs.SelectedItem = ProductsTab;
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
        _ = productSave.Now();
        LoadProduct(item.Product);
        if (recipe is null) return;
        var parts = Parts(id);
        model.Products.Origin = "Rezeptur aus der Prüfung. Erst mit „Übernehmen“ gilt sie im Katalog für alle Prüfungen.";
        model.Products.Recipe.Clear();
        foreach (var l in recipe) model.Products.Recipe.Add(new RecipeRow(parts, l));
    }

    void NewProduct(object? sender, RoutedEventArgs e)
    {
        NewProduct("", null);
        ProductName.Focus();
    }

    public void NewProduct(string name, Action<string>? created)
    {
        var f = model.Products;
        Tabs.SelectedItem = ProductsTab;
        _ = productSave.Now();
        productSave.Cancel();
        filling = true;
        ProductGrid.SelectedItem = null;
        productCreated = created;
        nameLeft = name;
        categoryLeft = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neues Produkt";
        f.Load(null, true);
        f.Name = name;
        f.Category.Load(Session.Categories(), null);
        if (created is not null) f.Recipe.Add(new RecipeRow(Parts(null), null));
        filling = false;
        productEdited = name != "";
        if (productEdited) productSave.Schedule();
    }

    void AddRecipeLine(object? sender, RoutedEventArgs e) =>
        model.Products.Recipe.Add(new RecipeRow(Parts(model.Products.CurrentId), null));

    void RemoveRecipeLine(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is RecipeRow row) model.Products.Recipe.Remove(row);
    }

    // What the user changed goes onto the product as it is stored now; the rest stays as it is there.
    // While the user is still at it, a line or name not filled in yet is waited for rather than pointed out.
    async Task<bool> SaveProduct(bool done, bool adopt = false)
    {
        var f = model.Products;
        if (!f.Active) return true;
        if (f.Origin != "" && !adopt) return false;
        var weight = Input.Int(f.Piece);
        var batch = Input.Int(f.Batch);
        var named = f.Name.Trim() != "";
        f.NameInvalid = !named && (done || f.CurrentId is not null);
        f.PieceInvalid = f.Piece.Trim() != "" && weight is not > 0;
        f.BatchInvalid = f.HasRecipe && batch is not > 0;
        var unnamed = f.Category.Creating && f.Category.NewName.Trim() == "";
        f.Category.Invalid = unnamed && done;
        foreach (var row in f.Recipe)
        {
            var started = done || row.Part is not null || row.Amount.Trim() != "";
            row.PartInvalid = started && row.Part is null;
            row.AmountInvalid = started && Input.Int(row.Amount) is not > 0;
        }
        var recipe = RecipeLines();
        if (!named || f.PieceInvalid || f.BatchInvalid || unnamed || recipe is null)
        {
            if (done)
                Session.Fail(Missing(
                    f.NameInvalid ? "Name" : null,
                    f.Category.Invalid ? "Name der neuen Kategorie" : null,
                    f.PieceInvalid ? "Stückgewicht (eine ganze Zahl größer als 0)" : null,
                    f.BatchInvalid ? "„Das Rezept ergibt“ (eine ganze Zahl größer als 0)" : null,
                    recipe is null ? "Rezept (Produkt und Menge größer als 0 je Zeile)" : null));
            return false;
        }
        if (!done && (f.CurrentId is null && f.Name != nameLeft || f.Category.Creating && f.Category.NewName != categoryLeft)) return false;
        var id = f.CurrentId ?? Ids.New();
        var stored = Session.Rules?.Products.GetValueOrDefault(id);
        if (stored is not null && stored.Unit != f.UnitCode && !UnitFree(id)) Relock(stored);
        var fresh = stored is null;
        var data = fresh ? new Product { Id = id } : Json.Copy(stored!);
        if (fresh || f.NameEdited) data.Name = f.Name.Trim();
        if (fresh || f.UnitEdited) data.Unit = f.UnitCode;
        if (f.HasRecipe && (fresh || f.BatchEdited)) data.Batch = batch!.Value;
        if (fresh || f.AliasesEdited) data.Aliases = AliasLines(f.Aliases);
        if (fresh || f.PieceEdited) data.Piece = weight is { } w ? new Piece(w, ProductForm.PieceUnits[f.PieceUnitIndex]) : null;
        if (fresh || RecipeEdited()) data.Recipe = recipe;
        List<IRuleEntity> put = [];
        if (fresh || CategoryEdited())
        {
            var (categoryId, made) = CategoryOf(f.Category);
            data.CategoryId = categoryId;
            if (made is not null) put.Add(made);
        }
        if (put.Count == 0 && !fresh && Json.Serialize(data) == Json.Serialize(stored)) return true;
        put.Add(data);
        var isNew = f.CurrentId is null;
        var created = isNew ? productCreated : null;
        var edits = productEdits;
        var saved = false;
        await Compose(async () =>
        {
            f.CurrentId = id;
            saved = await Session.Put(put, AtPage(ProductsTab, id), CancellationToken.None);
            if (!saved && isNew && f.CurrentId == id) f.CurrentId = null;
        });
        if (saved && edits == productEdits) productEdited = false;
        if (saved && created is not null)
        {
            if (productCreated == created) productCreated = null;
            created(id);
        }
        return saved;
    }

    async void AdoptRecipe(object? sender, RoutedEventArgs e)
    {
        if (!await SaveProduct(true, adopt: true)) return;
        filling = true;
        model.Products.Origin = "";
        filling = false;
        productSave.Cancel();
    }

    void ProductNameLeft(object? sender, RoutedEventArgs e)
    {
        nameLeft = model.Products.Name;
        _ = productSave.Now();
    }

    void CategoryNameLeft(object? sender, RoutedEventArgs e)
    {
        categoryLeft = model.Products.Category.NewName;
        _ = productSave.Now();
    }

    static List<string> AliasLines(string text) =>
        [.. text.Split('\n').Select(a => a.Trim()).Where(a => a != "")];

    async void DeleteProduct(object? sender, RoutedEventArgs e) => await Delete(model.Products, Entity.Product, ProductNew);

    // Hold the rebuild until the write is through, so the form is filled once from what was stored.
    async Task Compose(Func<Task> save)
    {
        saving++;
        try
        {
            await save();
        }
        finally
        {
            saving--;
        }
        Rebuild();
    }

    // The category picked, or the one to be made with the product; a name that exists picks that one.
    (string? Id, Category? Created) CategoryOf(CategoryPicker picker)
    {
        if (!picker.Creating) return (picker.Selected?.Id is { Length: > 0 } id ? id : null, null);
        var name = picker.NewName.Trim();
        if (Session.Categories().Find(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
            return (existing.Id, null);
        var made = new Category { Id = Ids.New(), Name = name };
        return (made.Id, made);
    }

    List<ScopeItem> YieldScopes(RuleSet rs, List<Product> products)
    {
        var byCategory = new Dictionary<string, List<YieldRule>>(StringComparer.Ordinal);
        var byProduct = new Dictionary<string, List<YieldRule>>(StringComparer.Ordinal);
        foreach (var y in rs.YieldRules.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ThenBy(r => r.Deduction))
        {
            var (map, key) = string.IsNullOrEmpty(y.ProductId) ? (byCategory, y.CategoryId ?? "") : (byProduct, y.ProductId);
            if (!map.TryGetValue(key, out var list)) map[key] = list = [];
            list.Add(y);
        }
        var categories = Session.Categories().Select(c => new ScopeItem(c.Id, false, c.Name, byCategory.GetValueOrDefault(c.Id, [])));
        var items = products.Select(p => new ScopeItem(p.Id, true, p.Name, byProduct.GetValueOrDefault(p.Id, [])));
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
        var same = f.Scope is { } open && open.Id == scope.Id && open.Product == scope.Product;
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
    async Task<bool> SaveYield(YieldRow row)
    {
        if (model.Yields.Scope is not { } scope) return true;
        var name = row.Name.Trim();
        var deduction = Input.Bp(row.Deduction);
        var rated = deduction is >= 0 and <= Bp.Full;
        if (row.Id is null && (name == "" || row.Deduction.Trim() == "")) return row.Blank;
        row.NameInvalid = name == "";
        row.DeductionInvalid = !rated;
        if (row.NameInvalid || row.DeductionInvalid) return false;
        var isNew = row.Id is null;
        var id = row.Id ?? Ids.New();
        var data = new YieldRule
        {
            Id = id,
            Name = name,
            CategoryId = scope.Product ? null : scope.Id,
            ProductId = scope.Product ? scope.Id : null,
            Deduction = deduction!.Value,
            Default = row.IsDefault,
        };
        row.Id = id;
        model.Yields.CurrentId = id;
        if (isNew) model.Yields.Rules.Add(NewYieldRow());
        // Only one rule of a scope is its default; the one it replaces is cleared in the same step.
        var at = AtPage(YieldsTab, id);
        var cleared = true;
        if (data.Default && Session.Rules is { } rs)
            foreach (var other in rs.YieldRules.Values.Where(r => r.Default && r.Id != id && r.ProductId == data.ProductId && r.CategoryId == data.CategoryId).ToList())
            {
                var off = Json.Copy(other);
                off.Default = false;
                cleared &= await Session.Put(off, at, CancellationToken.None);
            }
        if (cleared && await Session.Put(data, at, CancellationToken.None)) return true;
        if (!isNew) return false;
        row.Id = null;
        if (model.Yields.Rules.LastOrDefault() is { Id: null, Blank: true } extra && extra != row) model.Yields.Rules.Remove(extra);
        return false;
    }

    async void DeleteYield(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not YieldRow { Id: { } id } row) return;
        if (!await Confirmed(row.Name, Entity.YieldRule)) return;
        row.Save.Cancel();
        if (await Session.Delete(Entity.YieldRule, id, AtPage(YieldsTab, id), Ct) && ScopeGrid.SelectedItem is { } scope) ScopeGrid.ContainerFromItem(scope)?.Focus();
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
        gewerbeSave.Cancel();
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
        gewerbeSave.Cancel();
        var f = model.Gewerbe;
        filling = true;
        GewerbeGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neue Gewerbekennzahl";
        f.Kennzahl = f.Name = "";
        filling = false;
        GewerbeKennzahl.Focus();
    }

    async Task<bool> SaveGewerbe()
    {
        var f = model.Gewerbe;
        var data = new Gewerbezweig { Id = f.CurrentId ?? Ids.New(), Kennzahl = f.Kennzahl.Trim(), Name = f.Name.Trim() };
        var taken = Session.Gewerbezweige().Exists(g => g.Kennzahl == data.Kennzahl && g.Id != data.Id);
        f.KennzahlInvalid = !Gewerbe.Kennzahl(data.Kennzahl) || taken;
        f.NameInvalid = data.Name == "";
        if (f.KennzahlInvalid || f.NameInvalid) return false;
        f.CurrentId = data.Id;
        if (!await Session.Put(data, AtPage(GewerbeTab, data.Id), CancellationToken.None)) return false;
        if (f.CurrentId != data.Id) return true;
        f.Existing = true;
        f.Title = data.Kennzahl + " " + data.Name;
        return true;
    }

    async void DeleteGewerbe(object? sender, RoutedEventArgs e) => await Delete(model.Gewerbe, Entity.Gewerbezweig, GewerbeNew);

    public static List<ReportTemplate> Templates(RuleSet rs) =>
        [.. rs.Templates.Values.OrderByDescending(t => t.Default).ThenBy(t => t.Name, StringComparer.CurrentCulture)];

    void TemplateSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (loading || TemplateGrid.SelectedItem is not TemplateItem item) return;
        _ = templateSave.Now();
        LoadTemplate(item.Template);
    }

    void LoadTemplate(ReportTemplate t)
    {
        var f = model.Templates;
        templateSave.Cancel();
        filling = true;
        f.CurrentId = t.Id;
        f.Existing = f.Active = true;
        f.Title = t.Name;
        f.Name = t.Name;
        f.Source = t.Source;
        f.IsDefault = t.Default;
        filling = false;
    }

    // A copy of the standard, kept as soon as it is made.
    void NewTemplate(object? sender, RoutedEventArgs e)
    {
        var from = Session.Rules?.Template(null);
        var f = model.Templates;
        _ = templateSave.Now();
        templateSave.Cancel();
        filling = true;
        TemplateGrid.SelectedItem = null;
        f.CurrentId = null;
        f.Existing = false;
        f.Active = true;
        f.Title = "Neue Vorlage";
        f.Name = from is null ? "" : "Kopie von " + from.Name;
        f.Source = from?.Source ?? "";
        f.IsDefault = false;
        filling = false;
        templateSave.Schedule();
        TemplateName.Focus();
    }

    void TemplateSourceLeft(object? sender, RoutedEventArgs e) => _ = templateSave.Now();

    async Task<bool> SaveTemplate(bool done)
    {
        var f = model.Templates;
        if (!f.Active) return true;
        var id = f.CurrentId ?? Ids.New();
        var data = new ReportTemplate { Id = id, Name = f.Name.Trim(), Source = f.Source, Default = f.IsDefault };
        f.NameInvalid = data.Name == "";
        if (f.NameInvalid)
        {
            if (done) Session.Fail(Missing("Name"));
            return false;
        }
        if (Session.Rules?.Templates.GetValueOrDefault(id) is { } stored
            && stored.Name == data.Name && stored.Source == data.Source && stored.Default == data.Default) return true;
        f.CurrentId = id;
        if (!await Session.Put(data, AtPage(TemplatesTab, id), CancellationToken.None))
        {
            if (!f.Existing && f.CurrentId == id) f.CurrentId = null;
            return false;
        }
        if (f.CurrentId != id) return true;
        f.Existing = true;
        f.Title = data.Name;
        return true;
    }

    async void DeleteTemplate(object? sender, RoutedEventArgs e) => await Delete(model.Templates, Entity.Template, TemplateNew);
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed record KV(string Label, string Value);

public sealed record RevenueRow(string Vat, string Declared, string Calculated, string Difference, bool Total);

public sealed record MarkupRow(string Sparte, string CostOfGoods, string RevenueNet, string GrossProfit,
    string Markup, bool Total);

public sealed record ResultRow(string ProductId, string Name, string Portions, long PortionsValue, string Cost, string Revenue,
    long RevenueValue, string Markup, bool PriceMissing, bool Adjusted, string RecipeTip);

public sealed record RecipeUse(string Name, string Amount, string Note)
{
    public bool HasNote => Note != "";
}

public sealed record ProductDetail(string Name, List<KV> Facts, string Note)
{
    public bool HasNote => Note != "";
}

public sealed class CalcModel : Observable
{
    bool busy, hasResult, noInvoices, slow;
    ProductDetail? detail;
    RecipeEditor? editor;

    public ObservableCollection<ResultRow> Products { get; } = [];
    public ObservableCollection<RevenueRow> Revenue { get; } = [];
    public ObservableCollection<MarkupRow> Markups { get; } = [];
    public ObservableCollection<KV> Summary { get; } = [];
    public ObservableCollection<YieldKindGroup> Yields { get; } = [];
    public ExclusionModel Exclusions { get; } = new();
    public bool HasYields => Yields.Count > 0;
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public bool EmptyAssortment => Products.Count == 0;
    public ProductDetail? Detail { get => detail; set { if (Set(ref detail, value)) Raise(nameof(NoDetail)); } }
    public bool NoDetail => detail is null;
    public RecipeEditor? Editor { get => editor; set => Set(ref editor, value); }
    public bool HasResult { get => hasResult; set { if (Set(ref hasResult, value)) Raise(nameof(Calculating)); } }
    public bool NoInvoices { get => noInvoices; set { if (Set(ref noInvoices, value)) Raise(nameof(Calculating)); } }
    public bool Slow { get => slow; set { if (Set(ref slow, value)) Raise(nameof(Calculating)); } }
    public bool Calculating => slow && !hasResult && !noInvoices;

    public CalcModel()
    {
        Products.CollectionChanged += (_, _) => Raise(nameof(EmptyAssortment));
        Yields.CollectionChanged += (_, _) => Raise(nameof(HasYields));
    }
}

public partial class CalcView : Screen
{
    public override string Topic => Help.Calc;

    protected override int Page => (int)Tab.Calc;

    const string ProductItem = "product:", YieldItem = "yield:", RevenueItem = "revenue:", GroupItem = "group:";

    readonly CalcModel model = new();
    readonly DispatcherTimer timer = new();
    readonly HashSet<string> promoting = [];
    int generation;
    bool filling, choosing;
    string edited = "", revealing = "";
    (Case Case, RuleSet Rules, RuleSet Catalog, Report Report, List<ProductRow> Sold)? shown;

    public CalcView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        timer.Tick += (_, _) => _ = Flush();
        Mapping.Attach(session, () => Ct, key => At(GroupItem + key));
        Mapping.Assigned += _ => Reassigned();
    }

    protected override async void OnEnter()
    {
        if (!model.Slow) DispatcherTimer.RunOnce(() => model.Slow = true, TimeSpan.FromMilliseconds(250));
        revealing = Revealing() ?? "";
        if (revealing.StartsWith(ProductItem)) Session.WantedProduct = revealing[ProductItem.Length..];
        if (Session.Case is not null) await LoadRules();
    }

    protected override async void Render(RuleSet rs)
    {
        if (Session.Case is not { } kase) return;
        IngredientBox.SetCategoryNames(this, Session.CategoryNames);
        IngredientBox.SetSimilar(this, Session.SimilarIngredients);
        Mapping.Refresh();
        model.NoInvoices = kase.Invoices.Count == 0;
        if (model.NoInvoices)
        {
            model.HasResult = false;
            return;
        }
        ShowYields(kase, rs);
        if (Promote(kase, rs))
        {
            Schedule(0);
            return;
        }
        var item = revealing;
        revealing = "";
        await Recalculate(kase, rs);
        if (IsActive) Show(item);
    }

    // The product an undo came back to is picked in the result; the others are looked up here.
    void Show(string item)
    {
        if (item.StartsWith(YieldItem))
        {
            Pages.SelectedItem = YieldsPage;
            if (model.Yields.SelectMany(g => g.Rows).FirstOrDefault(r => r.Label == item[YieldItem.Length..]) is { } row) Reveal.Flash(this, row);
            return;
        }
        var ex = model.Exclusions;
        var excluded = ex.Unused.Concat(ex.Omitted).FirstOrDefault(r =>
            item == RevenueItem + r.IngredientId || item == GroupItem + r.Group.Key);
        if (excluded is null) return;
        Pages.SelectedItem = ExcludedPage;
        Reveal.Row(excluded.Why == Exclusion.Unused ? UnusedGrid : OmittedGrid, excluded);
    }

    protected override void OnLeave()
    {
        if (!timer.IsEnabled) return;
        timer.Stop();
        _ = Session.SaveCase(At(edited), CancellationToken.None);
    }

    bool Promote(Case kase, RuleSet rs)
    {
        var promoted = kase.Products.Where(cp => promoting.Contains(cp.ProductId) && cp.Recipe is { } own
            && rs.Products.TryGetValue(cp.ProductId, out var p) && Recipes.Same(own, Recipes.Flat(rs, p))).ToList();
        foreach (var cp in promoted)
        {
            promoting.Remove(cp.ProductId);
            Drop(cp);
            edited = ProductItem + cp.ProductId;
        }
        return promoted.Count > 0;
    }

    async void Reassigned() => await LoadRules();

    static void Drop(CaseProduct cp)
    {
        cp.Recipe = null;
        cp.RecipeBasis = 0;
    }

    void Schedule(int delayMs)
    {
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(delayMs, 1));
        timer.Start();
    }

    async Task Flush()
    {
        timer.Stop();
        if (!await Session.SaveCase(At(edited), CancellationToken.None))
        {
            Session.Fail("Rezeptur konnte nicht gespeichert werden");
            return;
        }
        if (Session.Case is { } kase && Session.Rules is { } rs && IsActive) await Recalculate(kase, rs);
    }

    async Task Recalculate(Case kase, RuleSet rs)
    {
        var g = ++generation;
        model.Busy = model.HasResult;
        var report = await Assortment.Calculate(Session, kase, rs, Ct);
        if (report is not null && g == generation) ShowResult(kase, rs, report);
        if (g == generation) model.Busy = false;
    }

    void ShowYields(Case kase, RuleSet rs)
    {
        model.Yields.Clear();
        foreach (var group in Yields.Groups(kase, rs, Session.Ingredients(), Session.CategoryName))
        {
            foreach (var row in group.Rows) row.Changed += () => YieldChosen(row);
            model.Yields.Add(group);
        }
    }

    async void YieldChosen(YieldGroupRow row)
    {
        if (Session.Case is not { } kase || Session.Rules is not { } rs) return;
        kase.Yields = Yields.Choices(model.Yields);
        if (!await Session.SaveCase(At(YieldItem + row.Label), CancellationToken.None))
        {
            Session.Fail("Ertragsregel konnte nicht gespeichert werden");
            return;
        }
        if (Session.Case is { } saved && IsActive) await Recalculate(saved, rs);
    }

    void ShowResult(Case kase, RuleSet catalog, Report r)
    {
        var rs = Recipes.Effective(kase, catalog);
        var listed = Assortment.Listed(kase);
        var sold = r.Products.Where(p => listed.Contains(p.ProductId))
            .OrderBy(p => Names.Product(rs, p.ProductId), StringComparer.CurrentCulture).ToList();
        var wanted = Session.WantedProduct;
        Session.WantedProduct = null;
        var selected = wanted ?? (ProductGrid.SelectedItem as ResultRow)?.ProductId;
        shown = (kase, rs, catalog, r, sold);
        filling = true;
        model.Products.Clear();
        foreach (var p in sold)
        {
            var own = kase.Products.Find(x => x.ProductId == p.ProductId);
            model.Products.Add(new ResultRow(p.ProductId, Names.Product(rs, p.ProductId), Format.Group(p.Portions), p.Portions,
                Format.Cents(p.CostPerPortion), p.PriceMissing ? "" : Format.Cents(p.RevenueNet), p.RevenueNet,
                p.CostOfGoods > 0 && !p.PriceMissing ? Format.Bp(p.Markup) : "", p.PriceMissing,
                own?.Recipe is not null, own is null ? "" : RecipeTip(rs, own)));
        }
        ProductGrid.SelectedItem = model.Products.FirstOrDefault(p => p.ProductId == selected);
        filling = false;
        ProductSelected(null, null);
        if (wanted is not null && ProductGrid.SelectedItem is { } item)
        {
            Pages.SelectedItem = PortionsPage;
            ProductGrid.ScrollIntoView(item, null);
            Reveal.Flash(ProductGrid, item);
        }
        var vat = VatRow.Of(kase, r);
        model.Revenue.Clear();
        foreach (var v in Revenue(vat)) model.Revenue.Add(v);
        model.Markups.Clear();
        foreach (var m in Markups(r)) model.Markups.Add(m);
        model.Summary.Clear();
        foreach (var kv in Summary(r)) model.Summary.Add(kv);
        ShowExclusions(kase, catalog, r);
        model.HasResult = true;
    }

    // A row that is still excluded for the same mapping keeps its detail, a note on a just assigned one stays.
    void ShowExclusions(Case kase, RuleSet catalog, Report r)
    {
        var ex = model.Exclusions;
        var kept = (UnusedGrid.SelectedItem ?? OmittedGrid.SelectedItem) as ExcludedRow;
        choosing = true;
        ex.Fill(r, kase, catalog);
        var again = kept is null ? null : ex.Unused.Concat(ex.Omitted).FirstOrDefault(x => x.Group.Key == kept.Group.Key);
        UnusedGrid.SelectedItem = again?.Why == Exclusion.Unused ? again : null;
        OmittedGrid.SelectedItem = again?.Why == Exclusion.Unused ? null : again;
        choosing = false;
        var lists = Lists.RowDefinitions;
        lists[1].Height = ex.HasUnused ? GridLength.Star : new GridLength(0);
        lists[3].Height = ex.HasOmitted ? GridLength.Star : new GridLength(0);
        if (again is null) Mapping.Show(null);
        else if (again.Group.MappingId != kept!.Group.MappingId || again.Why != kept.Why) Mapping.Show(again.Group);
    }

    void ExcludedSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (choosing || sender is not DataGrid grid) return;
        var row = grid.SelectedItem as ExcludedRow;
        if (row is null && (UnusedGrid.SelectedItem ?? OmittedGrid.SelectedItem) is not null) return;
        choosing = true;
        (grid == UnusedGrid ? OmittedGrid : UnusedGrid).SelectedItem = null;
        choosing = false;
        Mapping.Show(row?.Group);
    }

    async void ToggleRevenue(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not ExcludedRow { IngredientId: { } id } || Session.Case is not { } kase) return;
        if (!kase.NoRevenue.Remove(id))
        {
            kase.NoRevenue.Add(id);
            kase.NoRevenue.Sort(StringComparer.Ordinal);
        }
        if (!await Session.SaveCase(At(RevenueItem + id), CancellationToken.None))
        {
            Session.Fail("Festlegung konnte nicht gespeichert werden");
            return;
        }
        if (Session.Case is { } saved && Session.Rules is { } rs && IsActive) await Recalculate(saved, rs);
    }

    static string RecipeTip(RuleSet rs, CaseProduct cp)
    {
        if (cp.Recipe is null) return "";
        var lines = cp.Recipe.Select(l => RecipeEditor.Amount(l) + " " + Names.Ingredient(rs, l.IngredientId));
        return "Rezeptur nur dieser Prüfung:\n" + string.Join("\n", lines);
    }

    void ProductSelected(object? sender, SelectionChangedEventArgs? e)
    {
        if (filling) return;
        if (ProductGrid.SelectedItem is not ResultRow row || shown is not var (c, rs, catalog, r, sold)
            || sold.Find(p => p.ProductId == row.ProductId) is not { } p || !rs.Products.TryGetValue(p.ProductId, out var product))
        {
            model.Detail = null;
            model.Editor = null;
            return;
        }
        List<KV> facts = [new("Portionen", row.Portions), new("Einsatz je Portion", row.Cost)];
        if (!p.PriceMissing)
        {
            facts.Add(new("Bruttopreis", Format.Cents(p.GrossPrice)));
            facts.Add(new("Umsatz (netto)", row.Revenue));
        }
        if (row.Markup != "") facts.Add(new("Aufschlagsatz", row.Markup));
        var ingredients = r.Ingredients.ToDictionary(i => i.IngredientId);
        var recipe = product.Recipe.Select(l =>
        {
            var name = Names.Ingredient(rs, l.IngredientId);
            var amount = RecipeEditor.Amount(l);
            var note = Issue(c, rs, ingredients, sold, p, l) ?? (p.Binding.Contains(name) ? "begrenzt die Portionen" : "");
            return new RecipeUse(name, amount, note);
        }).ToList();
        var stuck = p.Portions == 0 && recipe.TrueForAll(x => x.Note == "");
        model.Detail = new ProductDetail(row.Name, facts, stuck ? "Keine ganze Portion möglich" : "");
        ShowRecipe(catalog, product, recipe);
    }

    // The editor outlives a recalculation, so a line being typed keeps its focus; only the notes move.
    void ShowRecipe(RuleSet catalog, Product product, List<RecipeUse> uses)
    {
        var cp = Listed(product.Id);
        var own = cp?.Recipe;
        var editor = model.Editor;
        if (editor is null || editor.ProductId != product.Id || editor.Adjusted != (own is not null))
        {
            editor = new RecipeEditor(product.Id) { Adjusted = own is not null };
            var options = Session.Ingredients();
            foreach (var l in own ?? []) editor.Add(new CaseRecipeRow(options, catalog, l));
            var edited = editor;
            edited.Edited += () => RecipeEdited(edited);
            model.Editor = editor;
        }
        editor.Uses = uses;
        editor.Stale = cp is not null && Recipes.Stale(cp, catalog);
        editor.Compare(catalog, CatalogRecipe(catalog, product.Id));
        var notes = new Dictionary<string, string>();
        for (var i = 0; i < product.Recipe.Count && i < uses.Count; i++) notes.TryAdd(product.Recipe[i].IngredientId, uses[i].Note);
        foreach (var row in editor.Rows) row.Note = row.Ingredient is { } ing ? notes.GetValueOrDefault(ing.Id, "") : "";
    }

    static List<RecipeLine> CatalogRecipe(RuleSet catalog, string productId) =>
        catalog.Products.TryGetValue(productId, out var p) ? Recipes.Flat(catalog, p) : [];

    CaseProduct? Listed(string productId) => Session.Case?.Products.Find(p => p.ProductId == productId);

    void RecipeEdited(RecipeEditor editor)
    {
        if (Listed(editor.ProductId) is not { Recipe: { } own } cp || shown is not { } s) return;
        var lines = editor.Lines();
        editor.Emptied = lines.Count == 0;
        editor.Compare(s.Catalog, CatalogRecipe(s.Catalog, editor.ProductId));
        if (lines.Count == 0 || Recipes.Same(lines, own)) return;
        cp.Recipe = lines;
        edited = ProductItem + cp.ProductId;
        Schedule(600);
    }

    void AdjustRecipe(object? sender, RoutedEventArgs e)
    {
        if (model.Editor is not { } editor || Listed(editor.ProductId) is not { } cp || shown is not { } s
            || !s.Catalog.Products.TryGetValue(editor.ProductId, out var p)) return;
        cp.Recipe = [.. Recipes.Flat(s.Catalog, p).Select(l => new RecipeLine { IngredientId = l.IngredientId, Amount = l.Amount, Unit = l.Unit })];
        cp.RecipeBasis = p.Meta.Rev;
        edited = ProductItem + cp.ProductId;
        ProductSelected(null, null);
        Schedule(0);
    }

    async void ResetRecipe(object? sender, RoutedEventArgs e)
    {
        if (model.Editor is not { } editor || Listed(editor.ProductId) is not { Recipe: not null }) return;
        var confirmed = await Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "Die Rezeptur dieser Prüfung geht verloren. Danach gilt wieder das Katalogrezept.",
            "Auf Katalog zurücksetzen");
        if (!confirmed || Listed(editor.ProductId) is not { } cp) return;
        promoting.Remove(cp.ProductId);
        Drop(cp);
        edited = ProductItem + cp.ProductId;
        ProductSelected(null, null);
        Schedule(0);
    }

    void PromoteRecipe(object? sender, RoutedEventArgs e)
    {
        if (model.Editor is not { } editor || Listed(editor.ProductId)?.Recipe is not { } own) return;
        promoting.Add(editor.ProductId);
        Session.EditProduct(editor.ProductId, [.. own.Select(l => new RecipeLine { IngredientId = l.IngredientId, Amount = l.Amount, Unit = l.Unit })]);
    }

    void EditCatalog(object? sender, RoutedEventArgs e)
    {
        if (model.Editor is { } editor) Session.EditProduct(editor.ProductId);
    }

    void AddRecipeLine(object? sender, RoutedEventArgs e)
    {
        if (model.Editor is { } editor && shown is { } s) editor.Add(new CaseRecipeRow(Session.Ingredients(), s.Catalog, null));
    }

    void RemoveRecipeLine(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not CaseRecipeRow row || model.Editor is not { } editor) return;
        editor.Rows.Remove(row);
        RecipeEdited(editor);
    }

    static string? Issue(Case c, RuleSet rs, Dictionary<string, IngredientRow> ingredients, List<ProductRow> sold, ProductRow product, RecipeLine line)
    {
        if (!ingredients.TryGetValue(line.IngredientId, out var ing))
            return Mapped(c, rs, line.IngredientId) ? "Gebindeinhalt fehlt in der Zuordnung" : "kein Einkauf zugeordnet";
        if (ing.Sellable <= 0) return "nach Bestand und Abzügen nichts verkaufsfähig";
        if (product.Portions == 0 && ing.Leftover < Scale.ToBase(line.Amount, line.Unit))
            return "aufgebraucht von " + Consumers(rs, sold, line.IngredientId);
        return null;
    }

    static bool Mapped(Case c, RuleSet rs, string ingredientId) =>
        c.Invoices.Any(inv => inv.Lines.Any(l => Match.Mapping(rs, inv.SupplierName, inv.Date, l)?.IngredientId == ingredientId));

    static string Consumers(RuleSet rs, List<ProductRow> sold, string ingredientId)
    {
        var users = sold
            .Where(p => p.Portions > 0 && rs.Products.TryGetValue(p.ProductId, out var x) && x.Recipe.Exists(l => l.IngredientId == ingredientId))
            .OrderByDescending(p => p.Portions * rs.Products[p.ProductId].Recipe.Where(l => l.IngredientId == ingredientId).Sum(l => Scale.ToBase(l.Amount, l.Unit)))
            .Select(p => p.Name)
            .ToList();
        return users.Count switch
        {
            0 => "andere Produkte",
            <= 3 => string.Join(", ", users),
            _ => string.Join(", ", users.Take(3)) + $" und {users.Count - 3} weitere",
        };
    }

    static List<KV> Summary(Report r)
    {
        var s = r.Totals;
        return
        [
            new("Erfasste Einkäufe (netto)", Format.Cents(s.Purchases)),
            new("Bestandsveränderung", Format.Cents(s.StockChange)),
            new("Wareneinsatz", Format.Cents(s.CostOfGoods)),
            new("davon Schwund und Abzüge", Format.Cents(s.ShrinkageCost)),
            new("davon nicht zugeteilte Ware", Format.Cents(s.UnallocatedCost)),
            new("Einsatz der verkauften Portionen", Format.Cents(s.AllocatedCost)),
            new("Einsatz mit geschätztem Umsatz", Format.Cents(s.EstimatedCost)),
            new("Geschätzter Umsatz", Format.Cents(s.EstimatedRevenueNet)),
        ];
    }

    static List<RevenueRow> Revenue(List<VatRow> rows) =>
        [.. rows.Select(v => new RevenueRow(v.Total ? "Summe" : Format.Bp(v.Vat),
            Format.Cents(v.Declared), Format.Cents(v.Calculated), Format.Cents(v.Difference), v.Total))];

    static List<MarkupRow> Markups(Report r) =>
    [
        .. r.Markups.Select(m => new MarkupRow(Format.Sparte(m.Sparte), Format.Cents(m.CostOfGoods),
            Format.Cents(m.RevenueNet), Format.Cents(m.GrossProfit), Format.Bp(m.Markup), false)),
        new("Gesamt", Format.Cents(r.Totals.PricedCost), Format.Cents(r.Totals.CalculatedRevenueNet),
            Format.Cents(r.Totals.GrossProfit), Format.Bp(r.Totals.Markup), true),
    ];

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);

    void GoProducts(object? sender, RoutedEventArgs e) => Session.Go(Tab.Products);
}

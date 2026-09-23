using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed record KV(string Label, string Value);

public sealed record RevenueRow(string Vat, string Declared, string Calculated, string Difference, bool Total);

public sealed record MarkupRow(string Sparte, string CostOfGoods, string RevenueNet, string GrossProfit,
    string Markup, bool Total);

public sealed record ResultRow(string ProductId, string Name, string Portions, long PortionsValue, string Cost, string Revenue,
    long RevenueValue, string Markup, bool PriceMissing);

public sealed record RecipeUse(string Name, string Amount, string Note)
{
    public bool HasNote => Note != "";
}

public sealed record ProductDetail(string Name, List<KV> Facts, List<RecipeUse> Recipe, string Note)
{
    public bool HasNote => Note != "";
}

public sealed class CalcModel : Observable
{
    bool busy, hasResult, noInvoices;
    ProductDetail? detail;

    public ObservableCollection<ResultRow> Products { get; } = [];
    public ObservableCollection<RevenueRow> Revenue { get; } = [];
    public ObservableCollection<MarkupRow> Markups { get; } = [];
    public ObservableCollection<KV> Summary { get; } = [];
    public ObservableCollection<YieldKindGroup> Yields { get; } = [];
    public bool HasYields => Yields.Count > 0;
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public bool EmptyAssortment => Products.Count == 0;
    public ProductDetail? Detail { get => detail; set { if (Set(ref detail, value)) Raise(nameof(NoDetail)); } }
    public bool NoDetail => detail is null;
    public bool HasResult { get => hasResult; set { if (Set(ref hasResult, value)) Raise(nameof(Calculating)); } }
    public bool NoInvoices { get => noInvoices; set { if (Set(ref noInvoices, value)) Raise(nameof(Calculating)); } }
    public bool Calculating => !hasResult && !noInvoices;

    public CalcModel()
    {
        Products.CollectionChanged += (_, _) => Raise(nameof(EmptyAssortment));
        Yields.CollectionChanged += (_, _) => Raise(nameof(HasYields));
    }
}

public partial class CalcView : Screen
{
    public override string Topic => Help.Calc;

    readonly CalcModel model = new();
    int generation;
    (Case Case, RuleSet Rules, Report Report, List<ProductRow> Sold)? shown;

    public CalcView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
    }

    protected override async void OnEnter()
    {
        if (Session.Case is null) return;
        await Session.LoadRules(Ct);
        if (Session.Case is not { } kase || Session.Rules is not { } rs || !IsActive) return;
        model.NoInvoices = kase.Invoices.Count == 0;
        if (model.NoInvoices)
        {
            model.HasResult = false;
            return;
        }
        ShowYields(kase, rs);
        await Recalculate(kase, rs);
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
            foreach (var row in group.Rows) row.Changed += YieldChosen;
            model.Yields.Add(group);
        }
    }

    async void YieldChosen()
    {
        if (Session.Case is not { } kase || Session.Rules is not { } rs) return;
        kase.Yields = Yields.Choices(model.Yields);
        if (!await Session.SaveCase(CancellationToken.None))
        {
            Session.Fail("Ertragsregel konnte nicht gespeichert werden");
            return;
        }
        if (Session.Case is { } saved && IsActive) await Recalculate(saved, rs);
    }

    void ShowResult(Case kase, RuleSet rs, Report r)
    {
        var listed = Assortment.Listed(kase);
        var sold = r.Products.Where(p => listed.Contains(p.ProductId))
            .OrderBy(p => Names.Product(rs, p.ProductId), StringComparer.CurrentCulture).ToList();
        var selected = (ProductGrid.SelectedItem as ResultRow)?.ProductId;
        shown = (kase, rs, r, sold);
        model.Products.Clear();
        foreach (var p in sold)
            model.Products.Add(new ResultRow(p.ProductId, Names.Product(rs, p.ProductId), Format.Group(p.Portions), p.Portions,
                Format.Cents(p.CostPerPortion), p.PriceMissing ? "" : Format.Cents(p.RevenueNet), p.RevenueNet,
                p.CostOfGoods > 0 && !p.PriceMissing ? Format.Bp(p.Markup) : "", p.PriceMissing));
        ProductGrid.SelectedItem = model.Products.FirstOrDefault(p => p.ProductId == selected);
        ProductSelected(null, null);
        model.Revenue.Clear();
        foreach (var v in Revenue(kase, r)) model.Revenue.Add(v);
        model.Markups.Clear();
        foreach (var m in Markups(r)) model.Markups.Add(m);
        model.Summary.Clear();
        foreach (var kv in Summary(r)) model.Summary.Add(kv);
        model.HasResult = true;
    }

    void ProductSelected(object? sender, SelectionChangedEventArgs? e)
    {
        if (ProductGrid.SelectedItem is not ResultRow row || shown is not var (c, rs, r, sold)
            || sold.Find(p => p.ProductId == row.ProductId) is not { } p || !rs.Products.TryGetValue(p.ProductId, out var product))
        {
            model.Detail = null;
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
            var amount = Format.Qty(Scale.ToBase(l.Amount, l.Unit), Units.Lookup(l.Unit)?.Base ?? Unit.Piece);
            var note = Issue(c, rs, ingredients, sold, p, l) ?? (p.Binding.Contains(name) ? "begrenzt die Portionen" : "");
            return new RecipeUse(name, amount, note);
        }).ToList();
        var stuck = p.Portions == 0 && recipe.TrueForAll(x => x.Note == "");
        model.Detail = new ProductDetail(row.Name, facts, recipe, stuck ? "Keine Portion passt in die Verteilung" : "");
    }

    static string? Issue(Case c, RuleSet rs, Dictionary<string, IngredientRow> ingredients, List<ProductRow> sold, ProductRow product, RecipeLine line)
    {
        if (!ingredients.TryGetValue(line.IngredientId, out var ing))
            return Mapped(c, rs, line.IngredientId) ? "Gebindeinhalt fehlt in der Zuordnung" : "kein Einkauf zugeordnet";
        if (ing.Sellable <= 0) return "nach Bestand und Abzügen nichts verkaufsfähig";
        if (product.Portions == 0 && ing.Leftover < Scale.ToBase(line.Amount, line.Unit))
            return "verteilt an " + Consumers(rs, sold, line.IngredientId);
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
            new("Wareneinsatz", Format.Cents(s.CostOfGoods)),
            new("davon Schwund und Abzüge", Format.Cents(s.ShrinkageCost)),
            new("davon nicht zugeteilte Ware", Format.Cents(s.UnallocatedCost)),
            new("Einsatz der verkauften Portionen", Format.Cents(s.AllocatedCost)),
            new("Rohgewinn", Format.Cents(s.GrossProfit)),
            new("Erfasste Einkäufe (netto)", Format.Cents(s.Purchases)),
            new("Bestandsveränderung", Format.Cents(s.StockChange)),
            new("Nicht berücksichtigt", $"{Format.Cents(s.UnmappedCost + s.UnusedCost)} ({Format.Bp(s.ExcludedShare)})"),
        ];
    }

    static List<RevenueRow> Revenue(Case c, Report r) =>
        [.. VatRow.Of(c, r).Select(v => new RevenueRow(v.Total ? "Summe" : Format.Bp(v.Vat),
            Format.Cents(v.Declared), Format.Cents(v.Calculated), Format.Cents(v.Difference), v.Total))];

    static List<MarkupRow> Markups(Report r) =>
    [
        .. r.Markups.Select(m => new MarkupRow(Format.Sparte(m.Sparte), Format.Cents(m.CostOfGoods),
            Format.Cents(m.RevenueNet), Format.Cents(m.GrossProfit), Format.Bp(m.Markup), false)),
        new("Gesamt", Format.Cents(r.Totals.AllocatedCost), Format.Cents(r.Totals.CalculatedRevenueNet),
            Format.Cents(r.Totals.GrossProfit), Format.Bp(r.Totals.Markup), true),
    ];

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);

    void GoProducts(object? sender, RoutedEventArgs e) => Session.Go(Tab.Products);
}

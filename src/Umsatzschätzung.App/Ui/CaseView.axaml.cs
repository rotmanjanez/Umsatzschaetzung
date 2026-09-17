using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.App.Ui;

public sealed class StockRow : Observable
{
    Ingredient? ingredient;
    string opening = "0", closing = "0";

    public StockRow(List<Ingredient> options) => Options = options;

    public List<Ingredient> Options { get; }
    public Ingredient? Ingredient { get => ingredient; set => Set(ref ingredient, value); }
    public string Opening { get => opening; set => Set(ref opening, value); }
    public string Closing { get => closing; set => Set(ref closing, value); }
}

public sealed class RuleOption(YieldRule rule, string text)
{
    public YieldRule Rule { get; } = rule;
    public string Text { get; } = text;
}

public sealed class YieldKindGroup(string kind, List<YieldGroupRow> rows) : Observable
{
    YieldGroupRow? selectedRow;

    public string Kind { get; } = kind;
    public List<YieldGroupRow> Rows { get; } = rows;
    public YieldGroupRow? SelectedRow { get => selectedRow; set => Set(ref selectedRow, value); }
}

public sealed class YieldGroupRow : Observable
{
    RuleOption? selected;

    public YieldGroupRow(YieldChoice choice, string label, List<YieldRule> rules)
    {
        Choice = choice;
        Label = label;
        Options = rules.Select(r => new RuleOption(r, r.Name)).ToList();
    }

    public YieldChoice Choice { get; }
    public string Label { get; }
    public List<RuleOption> Options { get; }
    // The detail list nulls its selection while it rebinds; a row always has a rule, so ignore that.
    public RuleOption? Selected { get => selected; set { if (value is not null) Set(ref selected, value); } }
}

public sealed class CaseModel : Observable
{
    public static readonly long[] VatValues = [1900, 700, 0];

    string label = "", from = "", to = "", name = "", taxNumber = "", pab = "";
    readonly string[] declared = ["", "", ""];
    YieldGroupRow? detail;
    bool noYields = true, noInvoices, syncing;

    public string Label { get => label; set => Set(ref label, value); }
    public string From { get => from; set => Set(ref from, value); }
    public string To { get => to; set => Set(ref to, value); }
    public string Name { get => name; set => Set(ref name, value); }
    public string TaxNumber { get => taxNumber; set => Set(ref taxNumber, value); }
    public string Pab { get => pab; set => Set(ref pab, value); }
    public string Declared19 { get => declared[0]; set => Set(ref declared[0], value); }
    public string Declared7 { get => declared[1]; set => Set(ref declared[1], value); }
    public string Declared0 { get => declared[2]; set => Set(ref declared[2], value); }
    public bool NoYields { get => noYields; private set => Set(ref noYields, value); }
    public bool NoInvoices { get => noInvoices; private set => Set(ref noInvoices, value); }
    public ObservableCollection<StockRow> Stock { get; } = [];
    public ObservableCollection<YieldKindGroup> Yields { get; } = [];

    // Which scope the right-hand column shows. View state, not case data — it deliberately
    // raises without Changed so browsing the list never marks the Prüfung dirty.
    public YieldGroupRow? Detail
    {
        get => detail;
        private set
        {
            if (ReferenceEquals(detail, value)) return;
            detail = value;
            Raise();
            Raise(nameof(NoDetail));
        }
    }

    public bool NoDetail => detail is null;

    public void Load(Session session)
    {
        var k = session.Case!;
        var d = session.Display!;
        Label = k.Label;
        From = d.PeriodFrom;
        To = d.PeriodTo;
        Name = k.Taxpayer.Name;
        TaxNumber = k.Taxpayer.TaxNumber;
        Pab = k.Taxpayer.PabNumber;
        NoInvoices = k.Invoices.Count == 0;
        Declared19 = Input.Edit(d.Declared.GetValueOrDefault(1900, ""));
        Declared7 = Input.Edit(d.Declared.GetValueOrDefault(700, ""));
        Declared0 = Input.Edit(d.Declared.GetValueOrDefault(0, ""));
        var ingredients = session.Ingredients();
        Stock.Clear();
        foreach (var e in k.Inventory)
            Stock.Add(new StockRow(ingredients)
            {
                Ingredient = ingredients.Find(i => i.Id == e.IngredientId),
                Opening = e.Opening.ToString(),
                Closing = e.Closing.ToString(),
            });
        Yields.Clear();
        Detail = null;
        foreach (var group in YieldGroups(session, ingredients))
        {
            foreach (var row in group.Rows) row.Selected = Chosen(k, row);
            group.Changed += () => Show(group);
            Yields.Add(group);
        }
        NoYields = Yields.Count == 0;
        if (Yields.Count > 0) Yields[0].SelectedRow = Yields[0].Rows[0];
    }

    // One selection across both lists: picking in one clears the other.
    void Show(YieldKindGroup active)
    {
        if (syncing) return;
        syncing = true;
        foreach (var g in Yields)
            if (!ReferenceEquals(g, active)) g.SelectedRow = null;
        syncing = false;
        Detail = active.SelectedRow;
    }

    public bool Collect(Case k)
    {
        k.Label = Label.Trim();
        var from = Input.Date(From);
        var to = Input.Date(To);
        if (from is { } f) k.PeriodFrom = f;
        if (to is { } t) k.PeriodTo = t;
        k.Taxpayer = new Taxpayer
        {
            Name = Name.Trim(),
            TaxNumber = TaxNumber.Trim(),
            PabNumber = Pab.Trim(),
        };
        var valid = k.Label != "" && from is not null && to is not null
            && k.Taxpayer.Name != "" && k.Taxpayer.TaxNumber != "" && k.Taxpayer.PabNumber != "";
        k.Declared = [];
        for (var i = 0; i < declared.Length; i++)
        {
            if (declared[i].Trim() == "") continue;
            var cents = Input.Cents(declared[i]);
            valid &= cents is not null;
            if (cents is > 0 or < 0) k.Declared.Add(new DeclaredRevenue { Vat = VatValues[i], Net = cents.Value });
        }
        k.Inventory = [];
        foreach (var row in Stock)
        {
            var opening = Input.Int(row.Opening);
            var closing = Input.Int(row.Closing);
            if (row.Ingredient is null || opening is null || closing is null) return false;
            k.Inventory.Add(new InventoryEntry { IngredientId = row.Ingredient.Id, Opening = opening.Value, Closing = closing.Value });
        }
        k.Yields = Yields
            .SelectMany(g => g.Rows)
            .Where(r => r.Selected is not null)
            .Select(r => new YieldChoice { IngredientId = r.Choice.IngredientId, CategoryId = r.Choice.CategoryId, YieldRuleId = r.Selected!.Rule.Id })
            .ToList();
        return valid;
    }

    // Only what this Prüfung actually touches: an empty café has no business being offered fuel or funerals.
    static List<YieldKindGroup> YieldGroups(Session session, List<Ingredient> ingredients)
    {
        if (session.Rules is null || session.Case is null) return [];
        var rs = session.Rules.RuleSet;
        var used = InCase(session.Case, rs);
        var rules = rs.YieldRules.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        List<YieldGroupRow> byCategory = [], byIngredient = [];
        var seen = new HashSet<string>();
        foreach (var ing in ingredients)
        {
            if (!used.Contains(ing.Id)) continue;
            if (ing.CategoryId != "" && seen.Add(ing.CategoryId))
            {
                var forCategory = rules.Where(r => string.IsNullOrEmpty(r.IngredientId) && r.CategoryId == ing.CategoryId).ToList();
                if (forCategory.Count > 1)
                    byCategory.Add(new YieldGroupRow(
                        new YieldChoice { CategoryId = ing.CategoryId }, session.CategoryName(ing.CategoryId), forCategory));
            }
            var forIngredient = rules.Where(r => r.IngredientId == ing.Id).ToList();
            if (forIngredient.Count > 1)
                byIngredient.Add(new YieldGroupRow(new YieldChoice { IngredientId = ing.Id }, ing.Name, forIngredient));
        }
        List<YieldKindGroup> groups = [];
        if (byCategory.Count > 0) groups.Add(new YieldKindGroup("Nach Kategorie", Sorted(byCategory)));
        if (byIngredient.Count > 0) groups.Add(new YieldKindGroup("Nach Zutat", Sorted(byIngredient)));
        return groups;
    }

    static List<YieldGroupRow> Sorted(List<YieldGroupRow> rows) =>
        rows.OrderBy(r => r.Label, StringComparer.Ordinal).ToList();

    static HashSet<string> InCase(Case k, RuleSet rs)
    {
        var ids = new HashSet<string>();
        foreach (var e in k.Inventory) ids.Add(e.IngredientId);
        foreach (var p in k.Products)
            if (rs.Products.TryGetValue(p.ProductId, out var product))
                foreach (var line in product.Recipe) ids.Add(line.IngredientId);
        foreach (var invoice in k.Invoices)
            foreach (var line in invoice.Lines)
                if (Match.Mapping(rs, invoice.SupplierName, invoice.Date, line) is { } m) ids.Add(m.IngredientId);
        return ids;
    }

    static RuleOption Chosen(Case k, YieldGroupRow row)
    {
        foreach (var y in k.Yields)
        {
            if (y.IngredientId != row.Choice.IngredientId || y.CategoryId != row.Choice.CategoryId) continue;
            var option = row.Options.Find(o => o.Rule.Id == y.YieldRuleId);
            if (option is not null) return option;
        }
        return row.Options.Find(o => o.Rule.Default) ?? row.Options[0];
    }
}

public partial class CaseView : Screen
{
    readonly CaseModel model = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    bool loading;

    public CaseView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        model.Changed += Edited;
        model.Stock.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (StockRow row in e.NewItems) row.Changed += Edited;
            Edited();
        };
        model.Yields.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (YieldKindGroup group in e.NewItems)
                foreach (var row in group.Rows) row.Changed += Edited;
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Save(Ct);
        };
    }

    protected override async void OnEnter()
    {
        if (Session.Case is null) return;
        await Session.LoadRules(Ct);
        if (Session.Case is null || !IsActive) return;
        loading = true;
        model.Load(Session);
        loading = false;
    }

    protected override void OnLeave()
    {
        timer.Stop();
        _ = Save(CancellationToken.None);
    }

    void Edited()
    {
        if (loading) return;
        timer.Stop();
        timer.Start();
    }

    async Task Save(CancellationToken ct)
    {
        var kase = Session.Case;
        if (kase is null || !model.Collect(kase)) return;
        if (!await Session.SaveCase(ct) && !ct.IsCancellationRequested)
            Session.Fail("Änderungen an der Prüfung konnten nicht gespeichert werden");
    }

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);

    void AddStock(object? sender, RoutedEventArgs e) => model.Stock.Add(new StockRow(Session.Ingredients()));

    void RemoveStock(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is StockRow row) model.Stock.Remove(row);
    }
}

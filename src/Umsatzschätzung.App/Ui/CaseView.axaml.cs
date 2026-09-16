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

public sealed class YieldGroupRow : Observable
{
    YieldRule? selected;

    public YieldGroupRow(YieldChoice choice, string label, string kind, List<YieldRule> rules)
    {
        Choice = choice;
        Label = label;
        Kind = kind;
        Rules = rules;
    }

    public YieldChoice Choice { get; }
    public string Label { get; }
    public string Kind { get; }
    public List<YieldRule> Rules { get; }
    public YieldRule? Selected { get => selected; set => Set(ref selected, value); }
}

public sealed class CaseModel : Observable
{
    public static readonly long[] VatValues = [1900, 700, 0];

    string label = "", from = "", to = "", name = "", taxNumber = "", pab = "";
    readonly string[] declared = ["", "", ""];
    bool noYields = true, noInvoices;

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
    public ObservableCollection<YieldGroupRow> Yields { get; } = [];

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
        foreach (var g in YieldGroups(session, ingredients))
        {
            g.Selected = Chosen(k, g);
            Yields.Add(g);
        }
        NoYields = Yields.Count == 0;
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
            .Where(g => g.Selected is not null)
            .Select(g => new YieldChoice { IngredientId = g.Choice.IngredientId, Category = g.Choice.Category, YieldRuleId = g.Selected!.Id })
            .ToList();
        return valid;
    }

    static List<YieldGroupRow> YieldGroups(Session session, List<Ingredient> ingredients)
    {
        var groups = new List<YieldGroupRow>();
        if (session.Rules is null) return groups;
        var rules = session.Rules.RuleSet.YieldRules.Values.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var seen = new HashSet<string>();
        foreach (var ing in ingredients)
        {
            if (ing.Category != "" && seen.Add(ing.Category))
            {
                var byCategory = rules.Where(r => string.IsNullOrEmpty(r.IngredientId) && r.Category == ing.Category).ToList();
                if (byCategory.Count > 1)
                    groups.Add(new YieldGroupRow(new YieldChoice { Category = ing.Category }, ing.Category, "Kategorie", byCategory));
            }
            var byIngredient = rules.Where(r => r.IngredientId == ing.Id).ToList();
            if (byIngredient.Count > 1)
                groups.Add(new YieldGroupRow(new YieldChoice { IngredientId = ing.Id }, ing.Name, "Zutat", byIngredient));
        }
        return groups.OrderBy(g => g.Kind, StringComparer.Ordinal).ToList();
    }

    static YieldRule Chosen(Case k, YieldGroupRow g)
    {
        foreach (var y in k.Yields)
        {
            if (y.IngredientId != g.Choice.IngredientId || y.Category != g.Choice.Category) continue;
            var rule = g.Rules.Find(r => r.Id == y.YieldRuleId);
            if (rule is not null) return rule;
        }
        return g.Rules.Find(r => r.Default) ?? g.Rules[0];
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
            if (e.NewItems is not null)
                foreach (YieldGroupRow row in e.NewItems) row.Changed += Edited;
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

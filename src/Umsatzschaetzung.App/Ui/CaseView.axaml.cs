using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class StockRow : Observable
{
    Ingredient? ingredient;
    string opening = "0", closing = "0";
    int unitIndex;

    public StockRow(List<Ingredient> options) => Options = options;

    public List<Ingredient> Options { get; }
    public List<string> Units { get; } = [.. RulesView.RecipeUnits.Select(Model.Units.Label)];
    public Ingredient? Ingredient { get => ingredient; set => Set(ref ingredient, value); }
    public int UnitIndex { get => unitIndex; set => Set(ref unitIndex, value); }
    public string Opening { get => opening; set => Set(ref opening, value); }
    public string Closing { get => closing; set => Set(ref closing, value); }
}

public sealed class CaseModel : Observable
{
    public static readonly long[] VatValues = [1900, 700, 0];

    string label = "", from = "", to = "", name = "", taxNumber = "", pab = "", gewerbe = "";
    readonly string[] declared = ["", "", ""];
    bool noInvoices;
    List<Gewerbezweig> gewerbezweige = [];

    public string Label { get => label; set => Set(ref label, value); }
    public string From { get => from; set => Set(ref from, value); }
    public string To { get => to; set => Set(ref to, value); }
    public string Name { get => name; set => Set(ref name, value); }
    public string TaxNumber { get => taxNumber; set => Set(ref taxNumber, value); }
    public string Pab { get => pab; set => Set(ref pab, value); }
    public string Gewerbe { get => gewerbe; set { if (Set(ref gewerbe, value)) Raise(nameof(GewerbeInvalid)); } }
    public bool GewerbeInvalid => gewerbe != "" && !gewerbezweige.Exists(g => g.Kennzahl == gewerbe);

    // Kein Set: neue Wahlmöglichkeiten sind keine Änderung an der Prüfung.
    public List<Gewerbezweig> Gewerbezweige
    {
        get => gewerbezweige;
        set
        {
            gewerbezweige = value;
            Raise();
            Raise(nameof(GewerbeInvalid));
        }
    }
    public string Declared19 { get => declared[0]; set => Set(ref declared[0], value); }
    public string Declared7 { get => declared[1]; set => Set(ref declared[1], value); }
    public string Declared0 { get => declared[2]; set => Set(ref declared[2], value); }
    public bool NoInvoices { get => noInvoices; private set => Set(ref noInvoices, value); }
    public ObservableCollection<StockRow> Stock { get; } = [];

    static string Declared(Case k, long vat) =>
        k.Declared.FindLast(d => d.Vat == vat) is { } d ? Format.Cents(d.Net) : "";

    public void Load(Session session)
    {
        var k = session.Case!;
        Label = k.Label;
        From = Format.Date(k.PeriodFrom);
        To = Format.Date(k.PeriodTo);
        Name = k.Taxpayer.Name;
        TaxNumber = k.Taxpayer.TaxNumber;
        Pab = k.Taxpayer.PabNumber;
        Gewerbezweige = session.Gewerbezweige();
        Gewerbe = k.Taxpayer.Gewerbe;
        NoInvoices = k.Invoices.Count == 0;
        Declared19 = Input.Edit(Declared(k, 1900));
        Declared7 = Input.Edit(Declared(k, 700));
        Declared0 = Input.Edit(Declared(k, 0));
        var ingredients = session.Ingredients();
        Stock.Clear();
        foreach (var e in k.Inventory)
            Stock.Add(new StockRow(ingredients)
            {
                Ingredient = ingredients.Find(i => i.Id == e.IngredientId),
                Opening = e.Opening.ToString(),
                Closing = e.Closing.ToString(),
                UnitIndex = Math.Max(Array.IndexOf(RulesView.RecipeUnits, e.Unit), 0),
            });
    }

    // Eine unbekannte Kennzahl, die schon in der Prüfung stand, hält das Speichern nicht auf.
    public bool Collect(Case k)
    {
        var gewerbeOk = !GewerbeInvalid || Gewerbe == k.Taxpayer.Gewerbe;
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
            Gewerbe = Gewerbe,
        };
        var valid = k.Label != "" && from is not null && to is not null
            && k.Taxpayer.Name != "" && k.Taxpayer.TaxNumber != "" && k.Taxpayer.PabNumber != ""
            && gewerbeOk;
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
            k.Inventory.Add(new InventoryEntry
            {
                IngredientId = row.Ingredient.Id,
                Opening = opening.Value,
                Closing = closing.Value,
                Unit = RulesView.RecipeUnits[row.UnitIndex],
            });
        }
        return valid;
    }
}

public partial class CaseView : Screen
{
    public override string Topic => Help.Case;

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
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Save(Ct);
        };
    }

    protected override async void OnEnter()
    {
        Session.RulesChanged += ShowGewerbe;
        if (Session.Case is null) return;
        await Session.LoadRules(Ct);
        if (Session.Case is null || !IsActive) return;
        loading = true;
        model.Load(Session);
        loading = false;
    }

    void ShowGewerbe() => model.Gewerbezweige = Session.Gewerbezweige();

    protected override void OnLeave()
    {
        Session.RulesChanged -= ShowGewerbe;
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

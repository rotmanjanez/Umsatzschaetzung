using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class AssortmentRow(string productId) : Observable
{
    public static readonly string[] VatNamesList = ["19 %", "7 %", "0 %"];

    string name = "", price = "";
    int vatIndex;
    bool priceMissing;

    public string ProductId { get; } = productId;
    public string[] VatNames => VatNamesList;
    public string Name { get => name; set => Set(ref name, value); }
    public string Price { get => price; set => Set(ref price, value); }
    public int VatIndex { get => vatIndex; set => Set(ref vatIndex, value); }
    public bool PriceMissing { get => priceMissing; set => Set(ref priceMissing, value); }
    public bool Adjusted { get; init; }
}

public sealed record ProductDraft(string Typed)
{
    public string Name => "„" + Typed + "“ neu erstellen";
}

public sealed class ProductsModel : Observable
{
    bool suggesting;

    public ObservableCollection<AssortmentRow> Rows { get; } = [];
    public ObservableCollection<SuggestionRow> Suggestions { get; } = [];
    public List<Product> Catalog { get; set; } = [];
    public bool EmptyAssortment => Rows.Count == 0;
    public bool Suggesting { get => suggesting; set { if (Set(ref suggesting, value)) Raise(nameof(NoSuggestions)); } }
    public bool NoSuggestions => !suggesting && Suggestions.Count == 0;

    public ProductsModel()
    {
        Rows.CollectionChanged += (_, _) => Raise(nameof(EmptyAssortment));
        Suggestions.CollectionChanged += (_, _) => Raise(nameof(NoSuggestions));
    }
}

public partial class ProductsView : Screen
{
    public override string Topic => Help.Products;

    readonly ProductsModel model = new();
    readonly DispatcherTimer timer = new();
    int generation;
    string dismissedCase = "";
    HashSet<string> dismissed = [];

    public ProductsView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Refresh();
        };
        CatalogBox.AsyncPopulator = (text, _) => Task.FromResult(Choices(text));
    }

    IEnumerable<object> Choices(string? text)
    {
        var name = (text ?? "").Trim();
        var hits = model.Catalog.Where(p => Matches(name, p.Name)).ToList<object>();
        if (name != "" && !model.Catalog.Exists(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            hits.Add(new ProductDraft(name));
        return hits;
    }

    static bool Matches(string? text, string name) =>
        (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));

    protected override async void OnEnter()
    {
        if (Session.Case is null) return;
        Session.RulesChanged += RulesChanged;
        await Session.LoadRules(Ct);
        if (Session.Case is null || Session.Rules is null || !IsActive) return;
        Load();
    }

    void RulesChanged() => model.Catalog = Session.Products();

    void Load()
    {
        if (Session.Case is not { } kase || Session.Rules is not { } rs) return;
        if (kase.Id != dismissedCase) (dismissedCase, dismissed) = (kase.Id, []);
        model.Rows.Clear();
        foreach (var p in kase.Products.OrderBy(p => Names.Product(rs, p.ProductId), StringComparer.CurrentCulture))
            model.Rows.Add(Row(rs, p));
        model.Suggestions.Clear();
        Schedule(0);
    }

    protected override void OnLeave()
    {
        Session.RulesChanged -= RulesChanged;
        timer.Stop();
        if (Session.Case is not null) _ = Session.SaveCase(CancellationToken.None);
    }

    void Schedule(int delayMs)
    {
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(delayMs, 1));
        timer.Start();
    }

    AssortmentRow Row(RuleSet rs, CaseProduct p)
    {
        var row = new AssortmentRow(p.ProductId)
        {
            Name = Names.Product(rs, p.ProductId),
            Price = p.GrossPrice > 0 ? Input.Edit(Format.Cents(p.GrossPrice)) : "",
            VatIndex = Math.Max(0, Array.IndexOf(CaseModel.VatValues, p.Vat)),
            PriceMissing = p.GrossPrice <= 0,
            Adjusted = p.Recipe is not null,
        };
        row.PropertyChanged += (_, e) => Edited(row, e.PropertyName);
        return row;
    }

    async Task Refresh()
    {
        if (Session.Case is not { } kase || Session.Rules is not { } rs) return;
        var g = ++generation;
        var withInvoices = kase.Invoices.Count > 0;
        model.Suggesting = withInvoices;
        if (await Session.SaveCase(Ct) && withInvoices && g == generation) await Session.Run(async () =>
        {
            var sold = await Assortment.Calculate(Session, kase, rs, Ct);
            if (sold is null || g != generation) return;
            var suggestions = await Assortment.Suggest(Session, kase, rs, sold, dismissed, Ct);
            if (suggestions is null || g != generation) return;
            model.Suggestions.Clear();
            foreach (var s in suggestions) model.Suggestions.Add(s);
        });
        if (g == generation) model.Suggesting = false;
    }

    void Edited(AssortmentRow row, string? property)
    {
        if (Session.Case is null) return;
        switch (property)
        {
            case nameof(AssortmentRow.Price):
                var cents = row.Price.Trim() == "" ? 0 : Input.Cents(row.Price);
                if (cents is null) return;
                Settings(row.ProductId).GrossPrice = cents.Value;
                row.PriceMissing = cents.Value <= 0;
                Schedule(600);
                break;
            case nameof(AssortmentRow.VatIndex):
                if (row.VatIndex < 0) return;
                Settings(row.ProductId).Vat = CaseModel.VatValues[row.VatIndex];
                Schedule(600);
                break;
        }
    }

    CaseProduct Settings(string productId)
    {
        var products = Session.Case!.Products;
        var cp = products.Find(p => p.ProductId == productId);
        if (cp is null)
        {
            cp = new CaseProduct { ProductId = productId, Vat = CaseModel.VatValues[0] };
            products.Add(cp);
        }
        return cp;
    }

    void Add(string productId)
    {
        if (Session.Case is null || Session.Rules is not { } rs || model.Rows.Any(r => r.ProductId == productId)) return;
        var row = Row(rs, Settings(productId));
        var at = 0;
        while (at < model.Rows.Count && StringComparer.CurrentCulture.Compare(model.Rows[at].Name, row.Name) < 0) at++;
        model.Rows.Insert(at, row);
        model.Suggestions.Clear();
        Schedule(0);
    }

    void CatalogSelected(object? sender, SelectionChangedEventArgs e) => CatalogPicked(sender, e);

    void CatalogPicked(object? sender, EventArgs e)
    {
        if (sender is not AutoCompleteBox box || box.IsDropDownOpen) return;
        if (box.SelectedItem is Product p && box.Text == p.Name) Add(p.Id);
        else if (box.SelectedItem is ProductDraft d && box.Text == d.Name && Session.Case is { } kase)
            Session.NewProduct(d.Typed, id => { if (Session.Case == kase) Add(id); });
        else return;
        box.SelectedItem = null;
        Dispatcher.UIThread.Post(() => box.Text = "");
    }

    void AcceptSuggestion(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SuggestionRow s) Add(s.ProductId);
    }

    void DismissSuggestion(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SuggestionRow s || Session.Case is null) return;
        dismissed.Add(s.ProductId);
        model.Suggestions.Remove(s);
        Schedule(0);
    }

    async void ExportCsv(object? sender, RoutedEventArgs e)
    {
        timer.Stop();
        if (Session.Case is null || !await Session.SaveCase(Ct) || Session.Case is not { } kase) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.ExportAssortment(kase.Id, Ct);
            await Session.SaveFile(resp.FileName, resp.Data, Session.CsvFilter);
        });
    }

    async void ImportCsv(object? sender, RoutedEventArgs e)
    {
        if (Session.Case is null || (await Session.PickFiles(Session.CsvFilter, false)).FirstOrDefault() is not { } file) return;
        await Session.Run(async () =>
        {
            var read = await Session.Service.ReadAssortment(file.Data, Ct);
            if (Session.Case is not { } kase || Session.Rules is not { } rs) return;
            var listed = kase.Products.DistinctBy(p => p.ProductId).ToDictionary(p => p.ProductId);
            var conflicts = read.Products
                .Where(p => listed.TryGetValue(p.ProductId, out var l) && (l.GrossPrice, l.Vat) != (p.GrossPrice, p.Vat))
                .Select(p => new AssortmentConflict(Names.Product(rs, p.ProductId), listed[p.ProductId], p))
                .OrderBy(c => c.Name, StringComparer.CurrentCulture)
                .ToList();
            HashSet<string>? take = conflicts.Count == 0 ? [] : await AssortmentConflicts.Ask(TopLevel.GetTopLevel(this) as Window, conflicts);
            if (take is null || Session.Case != kase) return;
            foreach (var p in read.Products)
            {
                if (!listed.TryGetValue(p.ProductId, out var l)) kase.Products.Add(p);
                else if (take.Contains(p.ProductId)) (l.GrossPrice, l.Vat) = (p.GrossPrice, p.Vat);
            }
            Load();
            if (read.Unknown.Count > 0) Session.Fail("Nicht im Katalog: " + string.Join(", ", read.Unknown));
        });
    }

    void ShowRecipe(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is AssortmentRow row) Session.OpenProduct(row.ProductId);
    }

    void RemoveFromAssortment(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AssortmentRow row || Session.Case is null) return;
        Session.Case.Products.RemoveAll(p => p.ProductId == row.ProductId);
        model.Rows.Remove(row);
        Schedule(0);
    }
}

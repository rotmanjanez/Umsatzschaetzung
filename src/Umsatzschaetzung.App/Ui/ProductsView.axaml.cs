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
}

public sealed class ProductsModel : Observable
{
    bool suggesting;
    List<Product> catalog = [];

    public ObservableCollection<AssortmentRow> Rows { get; } = [];
    public ObservableCollection<SuggestionRow> Suggestions { get; } = [];
    public List<Product> Catalog { get => catalog; set => Set(ref catalog, value); }
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

    public ProductsView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Refresh();
        };
        CatalogBox.ItemFilter = (text, item) => item is Product p && Matches(text, p.Name);
    }

    static bool Matches(string? text, string name) =>
        (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries).All(t => name.Contains(t, StringComparison.OrdinalIgnoreCase));

    protected override async void OnEnter()
    {
        if (Session.Case is null) return;
        await Session.LoadRules(Ct);
        if (Session.Case is null || Session.Rules is null || !IsActive) return;
        model.Catalog = Session.Products();
        Load();
    }

    void Load()
    {
        if (Session.Case is not { } kase || Session.Rules is not { } rs) return;
        model.Rows.Clear();
        foreach (var p in kase.Products.Where(p => !p.Disabled).OrderBy(p => Names.Product(rs, p.ProductId), StringComparer.CurrentCulture))
            model.Rows.Add(Row(rs, p));
        model.Suggestions.Clear();
        Schedule(0);
    }

    protected override void OnLeave()
    {
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
        await Session.Run(async () =>
        {
            var saved = await Session.Service.PutCase(kase, Ct);
            if (Session.Case == kase) Session.SetCase(saved);
            if (!withInvoices || g != generation) return;
            var sold = await Assortment.Calculate(Session, saved, rs, Ct);
            if (sold is null || g != generation) return;
            var suggestions = await Assortment.Suggest(Session, saved, rs, sold, Ct);
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
        var cp = Settings(productId);
        cp.Disabled = false;
        var row = Row(rs, cp);
        var at = 0;
        while (at < model.Rows.Count && StringComparer.CurrentCulture.Compare(model.Rows[at].Name, row.Name) < 0) at++;
        model.Rows.Insert(at, row);
        model.Suggestions.Clear();
        Schedule(0);
    }

    void CatalogPicked(object? sender, EventArgs e)
    {
        if (sender is not AutoCompleteBox box || box.SelectedItem is not Product p || box.Text != p.Name) return;
        Add(p.Id);
        Dispatcher.UIThread.Post(() =>
        {
            box.SelectedItem = null;
            box.Text = "";
        });
    }

    void AcceptSuggestion(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SuggestionRow s) Add(s.ProductId);
    }

    void DismissSuggestion(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not SuggestionRow s || Session.Case is null) return;
        Settings(s.ProductId).Disabled = true;
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
        timer.Stop();
        if (!await Session.SaveCase(Ct) || Session.Case is not { } kase) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.ImportAssortment(kase.Id, file.Data, Ct);
            Session.SetCase(resp.Case);
            Load();
            if (resp.Unknown.Count > 0) Session.Fail("Nicht im Katalog: " + string.Join(", ", resp.Unknown));
        });
    }

    void RemoveFromAssortment(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not AssortmentRow row || Session.Case is null) return;
        Session.Case.Products.RemoveAll(p => p.ProductId == row.ProductId);
        model.Rows.Remove(row);
        Schedule(0);
    }
}

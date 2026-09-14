using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class ProductRowModel : Observable
{
    public static readonly string[] VatNamesList = ["19 %", "7 %", "0 %"];

    string name = "", portions = "", price = "", revenue = "";
    int vatIndex;
    bool active = true, priceMissing, disabled;

    public ProductRowModel(string productId) => ProductId = productId;

    public string ProductId { get; }
    public string[] VatNames => VatNamesList;
    public string Name { get => name; set => Set(ref name, value); }
    public string Portions { get => portions; set => Set(ref portions, value); }
    public string Price { get => price; set => Set(ref price, value); }
    public int VatIndex { get => vatIndex; set => Set(ref vatIndex, value); }
    public string Revenue { get => revenue; set => Set(ref revenue, value); }
    public bool Active { get => active; set => Set(ref active, value); }
    public bool PriceMissing { get => priceMissing; set => Set(ref priceMissing, value); }
    public bool Disabled { get => disabled; set => Set(ref disabled, value); }
}

public sealed class PinnedRow(List<Product> options) : Observable
{
    Product? product;
    string portions = "", reason = "";

    public List<Product> Options { get; } = options;
    public Product? Product { get => product; set => Set(ref product, value); }
    public string Portions { get => portions; set => Set(ref portions, value); }
    public string Reason { get => reason; set => Set(ref reason, value); }
}

public sealed class CalcModel : Observable
{
    bool busy, hasResult;
    NodeDisplay? node;

    public ObservableCollection<ProductRowModel> Products { get; } = [];
    public ObservableCollection<PinnedRow> Pinned { get; } = [];
    public ObservableCollection<RevenueRow> Revenue { get; } = [];
    public ObservableCollection<NodeDisplay> Roots { get; } = [];
    public ObservableCollection<KV> Summary { get; } = [];
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public bool HasResult { get => hasResult; set { if (Set(ref hasResult, value)) Raise(nameof(NoResult)); } }
    public bool NoResult => !hasResult;
    public NodeDisplay? Node
    {
        get => node;
        set
        {
            if (!Set(ref node, value)) return;
            Raise(nameof(HasNode));
            Raise(nameof(NoNode));
        }
    }
    public bool HasNode => node is not null;
    public bool NoNode => node is null;
}

public partial class CalcView : Screen
{
    readonly CalcModel model = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    int generation;
    bool loading;

    public CalcView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Recalc();
        };
        model.Pinned.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null)
                foreach (PinnedRow row in e.NewItems) row.Changed += () => Schedule(600);
            if (!loading) Schedule(600);
        };
    }

    protected override async void OnEnter()
    {
        if (Session.Case is null) return;
        await Session.LoadRules(Ct);
        if (Session.Case is null || !IsActive) return;
        loading = true;
        var products = Session.Products();
        model.Pinned.Clear();
        foreach (var p in Session.Case.Pinned)
            model.Pinned.Add(new PinnedRow(products) { Product = products.Find(x => x.Id == p.ProductId), Portions = p.Portions.ToString(), Reason = p.Reason });
        loading = false;
        Schedule(0);
    }

    protected override void OnLeave()
    {
        timer.Stop();
        var kase = Session.Case;
        if (kase is null || !Collect(kase)) return;
        _ = Session.SaveCase(CancellationToken.None);
    }

    void Schedule(int delayMs)
    {
        timer.Stop();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(delayMs, 1));
        timer.Start();
    }

    bool Collect(Case k)
    {
        k.Pinned = [];
        var valid = true;
        foreach (var row in model.Pinned)
        {
            var n = Input.Int(row.Portions);
            if (row.Product is null || n is null)
            {
                valid = false;
                continue;
            }
            k.Pinned.Add(new PinnedPortions { ProductId = row.Product.Id, Portions = n.Value, Reason = row.Reason });
        }
        return valid;
    }

    async Task Recalc()
    {
        var kase = Session.Case;
        if (kase is null || !Collect(kase)) return;
        var g = ++generation;
        model.Busy = true;
        await Session.Run(async () =>
        {
            var saved = await Session.Service.PutCase(kase, Ct);
            if (Session.Case == kase) Session.SetCase(saved);
            var resp = await Session.Service.Calculate(kase.Id, Ct);
            if (g == generation) ShowResult(resp);
        });
        if (g == generation) model.Busy = false;
    }

    void ShowResult(ReportDisplay d)
    {
        loading = true;
        var same = d.Products.Count == model.Products.Count && d.Products.Select(p => p.ProductId).SequenceEqual(model.Products.Select(p => p.ProductId));
        if (!same)
        {
            model.Products.Clear();
            foreach (var p in d.Products)
            {
                var row = new ProductRowModel(p.ProductId);
                row.PropertyChanged += (_, e) => ProductEdited(row, e.PropertyName);
                model.Products.Add(row);
            }
        }
        for (var i = 0; i < d.Products.Count; i++)
        {
            var p = d.Products[i];
            var row = model.Products[i];
            row.Name = p.Name;
            row.Portions = p.Portions;
            row.Revenue = p.Revenue;
            row.Disabled = p.Disabled;
            row.PriceMissing = p.PriceMissing && !p.Disabled;
            if (!same)
            {
                row.Price = Input.Edit(p.GrossPrice);
                row.VatIndex = Math.Max(0, Array.IndexOf(ProductRowModel.VatNamesList, p.Vat));
                row.Active = !p.Disabled;
            }
        }
        model.Revenue.Clear();
        foreach (var r in d.Revenue) model.Revenue.Add(r);
        model.Summary.Clear();
        foreach (var kv in d.Summary) model.Summary.Add(kv);
        model.Roots.Clear();
        model.Roots.Add(d.Root);
        model.Node = null;
        model.HasResult = true;
        loading = false;
    }

    void ProductEdited(ProductRowModel row, string? property)
    {
        if (loading || Session.Case is null) return;
        switch (property)
        {
            case nameof(ProductRowModel.Price):
                var cents = row.Price.Trim() == "" ? 0 : Input.Cents(row.Price);
                if (cents is null) return;
                ProductSettings(row.ProductId).GrossPrice = cents.Value;
                Schedule(600);
                break;
            case nameof(ProductRowModel.VatIndex):
                if (row.VatIndex < 0) return;
                ProductSettings(row.ProductId).Vat = CaseModel.VatValues[row.VatIndex];
                Schedule(0);
                break;
            case nameof(ProductRowModel.Active):
                ProductSettings(row.ProductId).Disabled = !row.Active;
                Schedule(0);
                break;
        }
    }

    CaseProduct ProductSettings(string productId)
    {
        var products = Session.Case!.Products;
        var cp = products.Find(p => p.ProductId == productId);
        if (cp is null)
        {
            cp = new CaseProduct { ProductId = productId };
            products.Add(cp);
        }
        return cp;
    }

    void AddPinned(object sender, RoutedEventArgs e) => model.Pinned.Add(new PinnedRow(Session.Products()));

    void RemovePinned(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is PinnedRow row) model.Pinned.Remove(row);
    }

    void NodeSelected(object sender, RoutedPropertyChangedEventArgs<object> e) => model.Node = e.NewValue as NodeDisplay;
}

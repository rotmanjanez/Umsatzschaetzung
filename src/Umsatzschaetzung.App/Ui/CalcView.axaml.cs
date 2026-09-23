using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed record KV(string Label, string Value);

public sealed record RevenueRow(string Vat, string Declared, string Calculated, string Difference, bool Total);

public sealed record MarkupRow(string Sparte, string CostOfGoods, string RevenueNet, string GrossProfit,
    string Markup, bool Total);

public sealed class ProductRowModel : Observable
{
    public static readonly string[] VatNamesList = ["19 %", "7 %", "0 %"];

    string name = "", portions = "", price = "", revenue = "", cost = "", markup = "";
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
    public string Cost { get => cost; set => Set(ref cost, value); }
    public string Markup { get => markup; set => Set(ref markup, value); }
    public bool Active { get => active; set => Set(ref active, value); }
    public bool PriceMissing { get => priceMissing; set => Set(ref priceMissing, value); }
    public bool Disabled { get => disabled; set { if (Set(ref disabled, value)) Raise(nameof(Fade)); } }
    public double Fade => disabled ? 0.55 : 1;
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
    bool busy, hasResult, noInvoices;

    public ObservableCollection<ProductRowModel> Products { get; } = [];
    public ObservableCollection<PinnedRow> Pinned { get; } = [];
    public ObservableCollection<RevenueRow> Revenue { get; } = [];
    public ObservableCollection<MarkupRow> Markups { get; } = [];
    public ObservableCollection<KV> Summary { get; } = [];
    public bool Busy { get => busy; set => Set(ref busy, value); }
    public bool HasResult { get => hasResult; set { if (Set(ref hasResult, value)) Raise(nameof(Calculating)); } }
    public bool NoInvoices { get => noInvoices; set { if (Set(ref noInvoices, value)) Raise(nameof(Calculating)); } }
    public bool Calculating => !hasResult && !noInvoices;
}

public partial class CalcView : Screen
{
    public override string Topic => Help.Calc;

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
        model.NoInvoices = Session.Case.Invoices.Count == 0;
        if (model.NoInvoices)
        {
            model.HasResult = false;
            return;
        }
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

    void ShowResult(CalcResp calc)
    {
        var kase = Session.Case!;
        var rs = Session.Rules!;
        var r = calc.Report;
        loading = true;
        var same = r.Products.Count == model.Products.Count
            && r.Products.Select(p => p.ProductId).SequenceEqual(model.Products.Select(p => p.ProductId));
        if (!same)
        {
            model.Products.Clear();
            foreach (var p in r.Products)
            {
                var row = new ProductRowModel(p.ProductId);
                row.PropertyChanged += (_, e) => ProductEdited(row, e.PropertyName);
                model.Products.Add(row);
            }
        }
        for (var i = 0; i < r.Products.Count; i++)
        {
            var p = r.Products[i];
            var row = model.Products[i];
            row.Name = Names.Product(rs, p.ProductId);
            row.Portions = Format.Portions(p.Portions);
            row.Revenue = Format.Cents(p.RevenueNet);
            row.Cost = Format.Cents(p.CostPerPortion);
            row.Markup = p.CostOfGoods > 0 && !p.PriceMissing ? Format.Bp(p.Markup) : "";
            row.Disabled = p.Disabled;
            row.PriceMissing = p.PriceMissing && !p.Disabled;
            if (!same)
            {
                row.Price = Input.Edit(p.PriceMissing ? "" : Format.Cents(p.GrossPrice));
                row.VatIndex = Math.Max(0, Array.IndexOf(ProductRowModel.VatNamesList, Format.Bp(p.Vat)));
                row.Active = !p.Disabled;
            }
        }
        model.Revenue.Clear();
        foreach (var v in Revenue(kase, r)) model.Revenue.Add(v);
        model.Markups.Clear();
        foreach (var m in Markups(r)) model.Markups.Add(m);
        model.Summary.Clear();
        foreach (var kv in Summary(r)) model.Summary.Add(kv);
        model.HasResult = true;
        loading = false;
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
            new("Rohgewinnaufschlagsatz", Format.Bp(s.Markup)),
            new("Portionen gesamt", Format.Portions(s.Portions)),
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

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);

    void AddPinned(object? sender, RoutedEventArgs e) => model.Pinned.Add(new PinnedRow(Session.Products()));

    void RemovePinned(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is PinnedRow row) model.Pinned.Remove(row);
    }
}

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class LineRow : Observable
{
    string quantity, unit, name, unitPrice, lineNet, vat;
    string? flag, quantityFlag, unitFlag, nameFlag, unitPriceFlag, lineNetFlag, vatFlag;

    public LineRow(InvoiceLine line, LineDisplay display, int page, Dictionary<Field, OcrWord> cells)
    {
        Line = line;
        Page = page;
        Cells = cells;
        quantity = display.Quantity;
        unit = line.UnitCode;
        name = line.Name;
        unitPrice = display.UnitPrice;
        lineNet = display.LineNet;
        vat = display.Vat;
    }

    public InvoiceLine Line { get; }
    public int Page { get; }
    public Dictionary<Field, OcrWord> Cells { get; }

    public string Quantity { get => quantity; set { if (Set(ref quantity, value) && Input.Milli(value) is { } v) Line.Quantity = v; } }
    public string Unit { get => unit; set { if (Set(ref unit, value)) Line.UnitCode = value; } }
    public string Name { get => name; set { if (Set(ref name, value)) Line.Name = value; } }
    public string UnitPrice { get => unitPrice; set { if (Set(ref unitPrice, value) && Input.Micro(value) is { } v) Line.UnitPrice = v; } }
    public string LineNet { get => lineNet; set { if (Set(ref lineNet, value) && Input.Cents(value) is { } v) Line.LineNet = v; } }
    public string Vat { get => vat; set { if (Set(ref vat, value) && Input.Bp(value) is { } v) Line.Vat = v; } }

    public string? Flag => flag;
    public bool Flagged => flag is not null;
    public string? QuantityFlag => quantityFlag;
    public bool QuantityFlagged => quantityFlag is not null;
    public string? UnitFlag => unitFlag;
    public bool UnitFlagged => unitFlag is not null;
    public string? NameFlag => nameFlag;
    public bool NameFlagged => nameFlag is not null;
    public string? UnitPriceFlag => unitPriceFlag;
    public bool UnitPriceFlagged => unitPriceFlag is not null;
    public string? LineNetFlag => lineNetFlag;
    public bool LineNetFlagged => lineNetFlag is not null;
    public string? VatFlag => vatFlag;
    public bool VatFlagged => vatFlag is not null;

    public void SetFlags(IEnumerable<Flag> flags)
    {
        flag = quantityFlag = unitFlag = nameFlag = unitPriceFlag = lineNetFlag = vatFlag = null;
        foreach (var f in flags)
        {
            flag ??= f.Message;
            switch (f.Field)
            {
                case Field.Quantity: quantityFlag ??= f.Message; break;
                case Field.Unit: unitFlag ??= f.Message; break;
                case Field.Name: nameFlag ??= f.Message; break;
                case Field.UnitPrice: unitPriceFlag ??= f.Message; break;
                case Field.LineNet: lineNetFlag ??= f.Message; break;
                case Field.Vat: vatFlag ??= f.Message; break;
            }
        }
        foreach (var p in new[]
                 {
                     nameof(Flag), nameof(Flagged), nameof(QuantityFlag), nameof(QuantityFlagged), nameof(UnitFlag), nameof(UnitFlagged),
                     nameof(NameFlag), nameof(NameFlagged), nameof(UnitPriceFlag), nameof(UnitPriceFlagged), nameof(LineNetFlag),
                     nameof(LineNetFlagged), nameof(VatFlag), nameof(VatFlagged),
                 })
            Raise(p);
    }
}

public sealed class VerifyModel : Observable
{
    string supplier = "", vatId = "", number = "", date = "", netTotal = "", grossTotal = "";
    string? supplierFlag, numberFlag, dateFlag, netFlag, grossFlag;
    bool canConfirm;

    public string Supplier { get => supplier; set => Set(ref supplier, value); }
    public string VatId { get => vatId; set => Set(ref vatId, value); }
    public string Number { get => number; set => Set(ref number, value); }
    public string Date { get => date; set => Set(ref date, value); }
    public string NetTotal { get => netTotal; set => Set(ref netTotal, value); }
    public string GrossTotal { get => grossTotal; set => Set(ref grossTotal, value); }
    public bool CanConfirm { get => canConfirm; set => Set(ref canConfirm, value); }
    public string? SupplierFlag => supplierFlag;
    public bool SupplierFlagged => supplierFlag is not null;
    public string? NumberFlag => numberFlag;
    public bool NumberFlagged => numberFlag is not null;
    public string? DateFlag => dateFlag;
    public bool DateFlagged => dateFlag is not null;
    public string? NetFlag => netFlag;
    public string? GrossFlag => grossFlag;
    public ObservableCollection<string> HeaderFlags { get; } = [];
    public ObservableCollection<LineRow> Lines { get; } = [];

    public void SetHeaderFlags(IEnumerable<Flag> flags)
    {
        supplierFlag = numberFlag = dateFlag = netFlag = grossFlag = null;
        HeaderFlags.Clear();
        foreach (var f in flags)
        {
            HeaderFlags.Add(f.Message);
            switch (f.Field)
            {
                case Field.Supplier: supplierFlag ??= f.Message; break;
                case Field.InvoiceNumber: numberFlag ??= f.Message; break;
                case Field.InvoiceDate: dateFlag ??= f.Message; break;
                case Field.NetTotal: netFlag ??= f.Message; break;
                case Field.GrossTotal: grossFlag ??= f.Message; break;
            }
        }
        foreach (var p in new[]
                 {
                     nameof(SupplierFlag), nameof(SupplierFlagged), nameof(NumberFlag), nameof(NumberFlagged), nameof(DateFlag),
                     nameof(DateFlagged), nameof(NetFlag), nameof(GrossFlag),
                 })
            Raise(p);
    }
}

public partial class VerifyView : Screen
{
    static readonly HashSet<string> Blocking = ["line_total", "sum_net", "missing_field"];
    static readonly Field[] LineFields = [Field.Quantity, Field.Unit, Field.Name, Field.UnitPrice, Field.LineNet, Field.Vat];

    readonly VerifyModel model = new();
    readonly Action<CaseResp> onStored;
    readonly List<OcrPage> pages;
    Invoice draft;
    InvoiceDisplay display;
    List<Flag> flags = [];
    int currentPage = -1;
    int previewSeq;
    bool applying;

    public VerifyView(Session session, Invoice invoice, InvoiceDisplay invoiceDisplay, OcrResp? ocr, Action<CaseResp> onStored) : base(session)
    {
        InitializeComponent();
        this.onStored = onStored;
        draft = invoice;
        display = invoiceDisplay;
        pages = ocr?.Pages ?? [];
        DataContext = model;
        model.Changed += HeaderEdited;
        LoadDraft();
        if (pages.Count > 0)
        {
            foreach (var p in pages)
            {
                flags.AddRange(p.Flags);
                foreach (var l in p.Lines) flags.AddRange(l.Flags);
            }
            for (var i = 0; i < pages.Count; i++) PageSelect.Items.Add("Seite " + (i + 1));
            PageSelect.SelectedIndex = 0;
            PageSelect.Visibility = pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        else
        {
            OcrPane.Visibility = Visibility.Collapsed;
            Source.Visibility = Visibility.Visible;
        }
        ApplyFlags(flags);
    }

    protected override async void OnEnter()
    {
        if (pages.Count > 0 || Session.Case is null) return;
        var caseId = Session.Case.Id;
        Source.Show(null, "Beleg wird geladen …");
        var ok = await Session.Run(async () =>
        {
            var src = await Session.Service.InvoiceSource(caseId, draft.Id, Ct);
            Source.Show(src, src.FileName);
        });
        if (!ok && IsActive) Source.Show(null, "Kein Beleg gespeichert.");
    }

    void LoadDraft()
    {
        applying = true;
        model.Supplier = draft.SupplierName;
        model.VatId = draft.SupplierVatId ?? "";
        model.Number = draft.Number;
        model.Date = display.Date;
        model.NetTotal = display.NetTotal;
        model.GrossTotal = display.GrossTotal;
        foreach (var row in model.Lines) row.Changed -= LineEdited;
        model.Lines.Clear();
        var refs = pages.SelectMany((p, i) => p.Lines.Select(l => (Page: i, Line: (OcrLine?)l))).ToList();
        for (var i = 0; i < draft.Lines.Count; i++)
        {
            var d = i < display.Lines.Count ? display.Lines[i] : new LineDisplay("", "", "", "", "");
            var r = i < refs.Count ? refs[i] : (Page: Math.Max(currentPage, 0), Line: null);
            var row = new LineRow(draft.Lines[i], d, r.Page, r.Line?.Cells ?? []);
            row.Changed += LineEdited;
            model.Lines.Add(row);
        }
        applying = false;
    }

    void HeaderEdited()
    {
        if (applying) return;
        draft.SupplierName = model.Supplier;
        draft.SupplierVatId = model.VatId == "" ? null : model.VatId;
        draft.Number = model.Number;
        if (Input.Date(model.Date) is { } d) draft.Date = d;
    }

    void LineEdited()
    {
        if (!applying) _ = Preview();
    }

    Invoice Current()
    {
        draft.Lines = model.Lines.Select(r => r.Line).ToList();
        return draft;
    }

    async Task Preview()
    {
        if (Session.Case is null) return;
        var seq = ++previewSeq;
        var req = new VerifyReq(Session.Case.Id, Current(), false, false, null, null);
        await Session.Run(async () =>
        {
            var v = await Session.Service.VerifyInvoice(req, Ct);
            if (seq == previewSeq) ApplyTotals(v);
        });
    }

    void ApplyTotals(VerifyResp v)
    {
        draft.NetTotal = v.Invoice.NetTotal;
        draft.GrossTotal = v.Invoice.GrossTotal;
        display = display with { NetTotal = v.Display.NetTotal, GrossTotal = v.Display.GrossTotal };
        applying = true;
        model.NetTotal = display.NetTotal;
        model.GrossTotal = display.GrossTotal;
        applying = false;
        ApplyFlags(v.Flags);
    }

    void Apply(VerifyResp v)
    {
        draft = v.Invoice;
        display = v.Display;
        LoadDraft();
        ApplyFlags(v.Flags);
    }

    void ApplyFlags(List<Flag> all)
    {
        flags = all;
        model.SetHeaderFlags(all.Where(f => f.LineNo == 0));
        foreach (var row in model.Lines) row.SetFlags(all.Where(f => f.LineNo == row.Line.No && f.LineNo != 0));
        model.CanConfirm = !all.Any(f => Blocking.Contains(f.Code));
        RenderFlagged();
    }

    async void Confirm(object sender, RoutedEventArgs e)
    {
        if (Session.Case is null) return;
        Lines.CommitEdit(DataGridEditingUnit.Row, true);
        var req = new VerifyReq(Session.Case.Id, Current(), true, false, null, null);
        Session.Message = "Rechnung wird übernommen …";
        await Session.Run(async () =>
        {
            var v = await Session.Service.VerifyInvoice(req, Ct);
            if (!v.Accepted || v.Case is null)
            {
                Apply(v);
                Session.Message = "Nicht übernommen, bitte die markierten Werte korrigieren.";
                return;
            }
            Session.Message = "Rechnung übernommen";
            Session.Drafts.Remove(v.Invoice.Id);
            onStored(v.Case);
        });
    }

    void AddLine(object sender, RoutedEventArgs e)
    {
        var no = model.Lines.Count == 0 ? 0 : model.Lines.Max(r => r.Line.No);
        var row = new LineRow(new InvoiceLine { No = no + 1, PriceBaseQty = 1000 }, new LineDisplay("", "", "", "", ""), Math.Max(currentPage, 0), []);
        row.Changed += LineEdited;
        model.Lines.Add(row);
        _ = Preview();
    }

    void RemoveLine(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not LineRow row) return;
        row.Changed -= LineEdited;
        model.Lines.Remove(row);
        _ = Preview();
    }

    void CellChanged(object? sender, EventArgs e)
    {
        var cell = Lines.CurrentCell;
        if (cell.Item is not LineRow row || cell.Column is null || cell.Column.DisplayIndex >= LineFields.Length)
        {
            FocusCell(currentPage, null);
            return;
        }
        FocusCell(row.Page, row.Cells.GetValueOrDefault(LineFields[cell.Column.DisplayIndex])?.Box);
    }

    void HeaderFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (pages.Count == 0) return;
        var field = ReferenceEquals(sender, SupplierBox) || ReferenceEquals(sender, VatIdBox) ? Field.Supplier
            : ReferenceEquals(sender, NumberBox) ? Field.InvoiceNumber
            : Field.InvoiceDate;
        FocusCell(0, pages[0].Header.GetValueOrDefault(field)?.Box);
    }

    void PageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PageSelect.SelectedIndex >= 0) ShowPage(PageSelect.SelectedIndex);
    }

    void FocusCell(int page, Box? box)
    {
        if (pages.Count == 0) return;
        if (page != currentPage && page >= 0 && page < pages.Count) PageSelect.SelectedIndex = page;
        if (box is null)
        {
            FocusBox.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(FocusBox, box.X - 4);
        Canvas.SetTop(FocusBox, box.Y - 4);
        FocusBox.Width = box.W + 8;
        FocusBox.Height = box.H + 8;
        FocusBox.Visibility = Visibility.Visible;
    }

    void ShowPage(int index)
    {
        if (index == currentPage || index < 0 || index >= pages.Count) return;
        currentPage = index;
        var page = pages[index];
        var image = Images.Decode(page.Image);
        var w = page.Width > 0 ? page.Width : image.PixelWidth;
        var h = page.Height > 0 ? page.Height : image.PixelHeight;
        PageCanvas.Width = w;
        PageCanvas.Height = h;
        PageImage.Source = image;
        PageImage.Width = w;
        PageImage.Height = h;
        FocusBox.Visibility = Visibility.Collapsed;
        RenderFlagged();
    }

    void RenderFlagged()
    {
        Flagged.Children.Clear();
        if (currentPage < 0 || currentPage >= pages.Count) return;
        var page = pages[currentPage];
        var boxes = new List<Box>();
        foreach (var f in flags.Where(f => f.LineNo == 0 && f.Field is not null))
            if (page.Header.TryGetValue(f.Field!.Value, out var cell)) boxes.Add(cell.Box);
        foreach (var row in model.Lines.Where(r => r.Page == currentPage))
        foreach (var f in flags.Where(f => f.LineNo == row.Line.No && f.LineNo != 0 && f.Field is not null))
            if (row.Cells.TryGetValue(f.Field!.Value, out var cell)) boxes.Add(cell.Box);
        foreach (var b in boxes)
        {
            var rect = new Rectangle
            {
                Width = b.W + 6,
                Height = b.H + 6,
                Stroke = (System.Windows.Media.Brush)FindResource("WarningBrush"),
                Fill = (System.Windows.Media.Brush)FindResource("WarningSoftBrush"),
                StrokeThickness = 2,
                RadiusX = 3,
                RadiusY = 3,
            };
            Canvas.SetLeft(rect, b.X - 3);
            Canvas.SetTop(rect, b.Y - 3);
            Flagged.Children.Add(rect);
        }
    }
}

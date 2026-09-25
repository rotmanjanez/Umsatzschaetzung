using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public enum Checked { Automatic, Manual, Pending }

public static class Checks
{
    public static Checked Of(Invoice inv) =>
        inv.Verification is { } v ? (v.Auto ? Checked.Automatic : Checked.Manual)
        : inv.Source == Source.Scan ? Checked.Pending
        : Checked.Automatic;

    public static string Text(Invoice inv) =>
        inv.Verification is { } v
            ? (v.Auto ? "Ohne Durchsicht übernommen am " : "Durchgesehen am ") + Format.Timestamp(v.At)
            : inv.Source == Source.Scan ? "Durchsicht offen" : "Automatisch übernommen";
}

public sealed class LineRow : Observable
{
    string quantity, unit, name, unitPrice, lineNet, vat;
    string? rowFlag, quantityFlag, unitFlag, nameFlag, unitPriceFlag, lineNetFlag, vatFlag;

    public LineRow(InvoiceLine line, int page, Dictionary<Field, OcrWord> cells)
    {
        Line = line;
        Page = page;
        Cells = cells;
        quantity = Format.Milli(line.Quantity);
        unit = line.UnitCode;
        name = line.Name;
        unitPrice = Format.UnitPrice(line.UnitPrice, line.PriceBaseQty, line.UnitCode);
        lineNet = Format.Cents(line.LineNet);
        vat = Format.Bp(line.Vat);
    }

    public InvoiceLine Line { get; }
    public int Page { get; }
    public Dictionary<Field, OcrWord> Cells { get; }

    public string Quantity { get => quantity; set { if (Set(ref quantity, value) && Input.Milli(value) is { } v) Line.Quantity = v; } }
    public string Unit
    {
        get => unit;
        set
        {
            var code = Units.Lookup(value)?.Code ?? value;
            if (!Set(ref unit, code)) return;
            Line.UnitCode = code;
            Raise(nameof(UnitLabel));
        }
    }

    public string UnitLabel => Units.Label(unit);
    public string Name { get => name; set { if (Set(ref name, value)) Line.Name = value; } }
    public string UnitPrice { get => unitPrice; set { if (Set(ref unitPrice, value) && Input.Micro(value) is { } v) Line.UnitPrice = v; } }
    public string LineNet { get => lineNet; set { if (Set(ref lineNet, value) && Input.Cents(value) is { } v) Line.LineNet = v; } }
    public string Vat { get => vat; set { if (Set(ref vat, value) && Input.Bp(value) is { } v) Line.Vat = v; } }

    public string? RowFlag => rowFlag;
    public bool RowFlagged => rowFlag is not null;
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
        rowFlag = quantityFlag = unitFlag = nameFlag = unitPriceFlag = lineNetFlag = vatFlag = null;
        foreach (var f in flags)
        {
            switch (f.Field)
            {
                case Field.Quantity: quantityFlag ??= f.Message; break;
                case Field.Unit: unitFlag ??= f.Message; break;
                case Field.Name: nameFlag ??= f.Message; break;
                case Field.UnitPrice: unitPriceFlag ??= f.Message; break;
                case Field.LineNet: lineNetFlag ??= f.Message; break;
                case Field.Vat: vatFlag ??= f.Message; break;
                default: rowFlag = rowFlag is null ? f.Message : rowFlag + "\n" + f.Message; break;
            }
        }
        foreach (var p in new[]
                 {
                     nameof(RowFlag), nameof(RowFlagged),
                     nameof(QuantityFlag), nameof(QuantityFlagged), nameof(UnitFlag), nameof(UnitFlagged),
                     nameof(NameFlag), nameof(NameFlagged), nameof(UnitPriceFlag), nameof(UnitPriceFlagged), nameof(LineNetFlag),
                     nameof(LineNetFlagged), nameof(VatFlag), nameof(VatFlagged),
                 })
            Raise(p);
    }
}

// A total as the document prints it, next to what its positions add up to.
public sealed class TotalRow(string label, Field of) : Observable
{
    string stated = "", sum = "";
    string? flag;

    public string Label => label;
    public Field Field => of;
    public long? Value { get; private set; }
    public string Stated { get => stated; set { if (Set(ref stated, value)) Value = Input.Cents(value); } }
    public string Sum { get => sum; set => Set(ref sum, value); }
    public string? Flag { get => flag; set { if (Set(ref flag, value)) Raise(nameof(Flagged)); } }
    public bool Flagged => flag is not null;

    public void Show(long? value, long sum)
    {
        Value = value;
        Set(ref stated, value is { } v ? Format.Cents(v) : "", nameof(Stated));
        Sum = Format.Cents(sum);
    }
}

public sealed class InvoiceModel : Observable
{
    string supplier = "", number = "", date = "", fileName = "", stateText = "", periodHint = "";
    Checked state = Checked.Pending;
    bool valid, saving, dirty, outsidePeriod;

    public string Supplier { get => supplier; set => Set(ref supplier, value); }
    public string Number { get => number; set => Set(ref number, value); }
    public string Date { get => date; set => Set(ref date, value); }
    public string FileName { get => fileName; set => Set(ref fileName, value); }
    public string PeriodHint { get => periodHint; set => Set(ref periodHint, value); }
    public bool OutsidePeriod { get => outsidePeriod; set => Set(ref outsidePeriod, value); }

    public Checked State
    {
        get => state;
        set
        {
            if (!Set(ref state, value)) return;
            foreach (var p in new[] { nameof(IsAutomatic), nameof(IsManual), nameof(IsPending), nameof(CanAccept) }) Raise(p);
        }
    }

    public string StateText { get => stateText; set => Set(ref stateText, value); }
    public bool IsAutomatic => state == Checked.Automatic;
    public bool IsManual => state == Checked.Manual;
    public bool IsPending => state == Checked.Pending;

    public bool Valid { get => valid; set { if (Set(ref valid, value)) Raise(nameof(CanAccept)); } }
    public bool Dirty { get => dirty; set { if (Set(ref dirty, value)) Raise(nameof(CanExport)); } }
    public bool Saving { get => saving; set { if (Set(ref saving, value)) { Raise(nameof(CanAccept)); Raise(nameof(CanExport)); } } }

    public bool CanAccept => valid && !saving && state != Checked.Manual;
    public bool CanExport => !dirty && !saving;

    public TotalRow Net { get; } = new("Netto", Field.NetTotal);
    public TotalRow Gross { get; } = new("Brutto", Field.GrossTotal);
    public ObservableCollection<string> HeaderFlags { get; } = [];
    public ObservableCollection<LineRow> Lines { get; } = [];
    public TotalRow[] Totals { get; }

    public InvoiceModel() => Totals = [Net, Gross];

    public void SetHeaderFlags(IEnumerable<Flag> flags)
    {
        string? net = null, gross = null;
        HeaderFlags.Clear();
        foreach (var f in flags)
        {
            if (!HeaderFlags.Contains(f.Message)) HeaderFlags.Add(f.Message);
            switch (f.Field)
            {
                case Field.NetTotal: net ??= f.Message; break;
                case Field.GrossTotal: gross ??= f.Message; break;
            }
        }
        Net.Flag = net;
        Gross.Flag = gross;
    }
}

// One editor for every invoice, whether it still awaits review or was taken over long ago: the
// document sits next to the values, and the values it was read from light up on the document.
public partial class InvoiceView : Screen
{
    public override string Topic => Help.Invoice;

    static readonly Field[] LineFields = [Field.Quantity, Field.Unit, Field.Name, Field.UnitPrice, Field.LineNet, Field.Vat];

    static readonly string[] HeaderFields =
        [nameof(InvoiceModel.Supplier), nameof(InvoiceModel.Number), nameof(InvoiceModel.Date)];

    const double SideBySideAt = 1150;

    readonly InvoiceModel model = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly Action<Case> onSaved;
    List<OcrPage> pages;
    Invoice invoice;
    List<Flag> flags = [];
    int currentPage = -1;
    int edits;
    Task stored = Task.CompletedTask;
    bool applying, sourceLoaded, checkedOnce, sideBySide;


    public InvoiceView(Session session, Invoice stored, OcrResp? ocr, Action<Case> onSaved) : base(session)
    {
        InitializeComponent();
        this.onSaved = onSaved;
        invoice = Copy(stored);
        pages = ocr?.Pages ?? [];
        DataContext = model;
        model.PropertyChanged += HeaderEdited;
        model.Net.PropertyChanged += TotalEdited;
        model.Gross.PropertyChanged += TotalEdited;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Store();
        };
        Load();
        if (pages.Count > 0)
        {
            foreach (var p in pages)
            {
                flags.AddRange(p.Flags);
                foreach (var l in p.Lines) flags.AddRange(l.Flags);
            }
            Show();
        }
        else
        {
            OcrPane.IsVisible = false;
            Source.IsVisible = true;
        }
        ApplyFlags(flags);
        SizeChanged += (_, e) => Arrange(e.NewSize.Width >= SideBySideAt);
    }

    // Stacked the scan and the lines share the height; once the window is wide enough both get all
    // of it and split the width instead.
    void Arrange(bool side)
    {
        if (side == sideBySide) return;
        sideBySide = side;
        Panes.RowDefinitions = side
            ? [new RowDefinition(GridLength.Star)]
            : [new RowDefinition(11, GridUnitType.Star) { MinHeight = 240 },
               new RowDefinition(new GridLength(6)),
               new RowDefinition(9, GridUnitType.Star) { MinHeight = 120 }];
        Panes.ColumnDefinitions = side
            ? [new ColumnDefinition(11, GridUnitType.Star) { MinWidth = 520 },
               new ColumnDefinition(new GridLength(6)),
               new ColumnDefinition(9, GridUnitType.Star) { MinWidth = 340 }]
            : [new ColumnDefinition(GridLength.Star)];
        Place(FormPane, 0);
        Place(PaneSplit, 1);
        Place(ScanPane, 2);
        PaneSplit.ResizeDirection = side ? GridResizeDirection.Columns : GridResizeDirection.Rows;
        PaneSplit.Width = side ? 6 : double.NaN;
        PaneSplit.Height = side ? double.NaN : 6;
        if (pages.Count > 0) Dispatcher.UIThread.Post(FitPage);
    }

    void Place(Control pane, int index)
    {
        Grid.SetRow(pane, sideBySide ? 0 : index);
        Grid.SetColumn(pane, sideBySide ? index : 0);
    }

    void Show()
    {
        PageSelect.Items.Clear();
        for (var i = 0; i < pages.Count; i++) PageSelect.Items.Add("Seite " + (i + 1));
        PageSelect.IsVisible = pages.Count > 1;
        OcrPane.IsVisible = true;
        Source.IsVisible = false;
        currentPage = -1;
        PageSelect.SelectedIndex = 0;
    }

    public string Id => invoice.Id;

    // Worth keeping around once the user leaves it: edits on their way, or a review still to be done.
    public bool Keep => model.Dirty || model.State == Checked.Pending;

    protected override async void OnEnter()
    {
        if (!checkedOnce)
        {
            checkedOnce = true;
            await Check();
        }
        if (pages.Count > 0 || sourceLoaded || Session.Case is null) return;
        if (await Read(Session.Case.Id)) return;
        if (Session.Sources.TryGetValue(invoice.Id, out var cached))
        {
            Source.Show(cached, cached.FileName);
            sourceLoaded = true;
            return;
        }
        var caseId = Session.Case.Id;
        Source.Show(null, "Beleg wird geladen …");
        var ok = await Session.Run(async () =>
        {
            var src = await Session.Service.InvoiceSource(caseId, invoice.Id, Ct);
            Session.Sources[invoice.Id] = src;
            Source.Show(src, src.FileName);
            sourceLoaded = true;
        });
        if (!ok && IsActive) Source.Show(null, "Kein Beleg gespeichert.");
    }

    // A scan keeps its reading with the case, so an invoice read long ago can still show where each
    // of its values was found. The rows are built again for their cells to learn their boxes.
    async Task<bool> Read(string caseId)
    {
        var read = new List<OcrPage>();
        await Session.Run(async () => read = (await Session.Service.InvoiceReading(caseId, invoice.Id, Ct)).Pages);
        if (read.Count == 0 || !IsActive) return false;
        Session.Readings[invoice.Id] = new OcrResp(invoice.Id, read, invoice);
        pages = read;
        Load();
        Show();
        ApplyFlags(flags);
        return true;
    }

    // The case keeps the stored invoice; edits stay in this copy until they are stored.
    static Invoice Copy(Invoice inv) => Json.Deserialize<Invoice>(Json.Serialize(inv));

    void Load()
    {
        applying = true;
        model.Supplier = invoice.SupplierName;
        model.Number = invoice.Number;
        model.Date = Format.Date(invoice.Date);
        ShowTotals();
        model.FileName = invoice.FileName;
        model.State = Checks.Of(invoice);
        model.StateText = Checks.Text(invoice);
        ShowPeriod();
        foreach (var row in model.Lines) row.Changed -= LineEdited;
        model.Lines.Clear();
        var refs = pages.SelectMany((p, i) => p.Lines.Select(l => (Page: i, Line: (OcrLine?)l))).ToList();
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            var r = i < refs.Count ? refs[i] : (Page: Math.Max(currentPage, 0), Line: null);
            var row = new LineRow(invoice.Lines[i], r.Page, r.Line?.Cells ?? []);
            row.Changed += LineEdited;
            model.Lines.Add(row);
        }
        applying = false;
    }

    void ShowPeriod()
    {
        model.OutsidePeriod = Session.Case is { } k && invoice.Date is { } d && (d < k.PeriodFrom || d > k.PeriodTo);
        model.PeriodHint = "Prüfungszeitraum " + Session.Period;
    }

    void HeaderEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (applying || Array.IndexOf(HeaderFields, e.PropertyName) < 0) return;
        invoice.SupplierName = model.Supplier;
        invoice.Number = model.Number;
        if (Input.Date(model.Date) is { } d) invoice.Date = d;
        ShowPeriod();
        Schedule();
    }

    void ShowTotals()
    {
        var was = applying;
        applying = true;
        model.Net.Show(invoice.StatedNet, invoice.NetTotal);
        model.Gross.Show(invoice.StatedGross, invoice.GrossTotal);
        applying = was;
    }

    void TotalEdited(object? sender, PropertyChangedEventArgs e)
    {
        if (applying || e.PropertyName != nameof(TotalRow.Stated)) return;
        invoice.StatedNet = model.Net.Value;
        invoice.StatedGross = model.Gross.Value;
        Schedule();
    }

    void LineEdited()
    {
        if (!applying) Schedule();
    }

    void Schedule()
    {
        edits++;
        model.Dirty = true;
        timer.Stop();
        timer.Start();
    }

    Invoice Current()
    {
        invoice.Lines = model.Lines.Select(r => r.Line).ToList();
        return invoice;
    }

    async Task Check()
    {
        if (Session.Case is null) return;
        var req = new VerifyReq(Session.Case.Id, Current(), Intent.Check, null, null);
        await Session.Run(async () => ApplyTotals(await Session.Service.VerifyInvoice(req, Ct)));
    }

    // Every edit is stored as it settles, one write after the other so an older one never lands
    // last. Storing takes back an earlier review; only accepting marks the invoice as reviewed.
    Task Store() => stored = StoreAfter(stored);

    async Task StoreAfter(Task prior)
    {
        await prior;
        if (Session.Case is null || !model.Dirty) return;
        var seq = edits;
        var req = new VerifyReq(Session.Case.Id, Copy(Current()), Intent.Store, null, null);
        await Session.Run(async () =>
        {
            var v = await Session.Service.VerifyInvoice(req, CancellationToken.None);
            invoice.Verification = v.Invoice.Verification;
            model.State = Checks.Of(invoice);
            model.StateText = Checks.Text(invoice);
            if (seq == edits)
            {
                ApplyTotals(v);
                model.Dirty = false;
            }
            if (v.Case is not null) onSaved(v.Case);
        });
    }

    protected override void OnLeave()
    {
        Lines.CommitEdit(DataGridEditingUnit.Row, true);
        Totals.CommitEdit(DataGridEditingUnit.Row, true);
        if (!timer.IsEnabled) return;
        timer.Stop();
        _ = Store();
    }

    void ApplyTotals(VerifyResp v)
    {
        invoice.NetTotal = v.Invoice.NetTotal;
        invoice.GrossTotal = v.Invoice.GrossTotal;
        ShowTotals();
        ApplyFlags(v.Flags, v.Blocked);
    }

    void Apply(VerifyResp v)
    {
        invoice = Copy(v.Invoice);
        Load();
        ApplyFlags(v.Flags, v.Blocked);
    }

    // Whether a flag blocks is the service's call: on open the reading's own flags are only drawn,
    // and the first check that comes back decides.
    void ApplyFlags(List<Flag> all, bool? blocked = null)
    {
        flags = all;
        model.SetHeaderFlags(all.Where(f => f.LineNo == 0));
        foreach (var row in model.Lines) row.SetFlags(all.Where(f => f.LineNo == row.Line.No && f.LineNo != 0));
        if (blocked is { } b) model.Valid = !b;
        RenderFlagged();
    }

    async void Accept(object? sender, RoutedEventArgs e)
    {
        if (Session.Case is not { } kase) return;
        Lines.CommitEdit(DataGridEditingUnit.Row, true);
        Totals.CommitEdit(DataGridEditingUnit.Row, true);
        timer.Stop();
        model.Saving = true;
        await Store();
        var req = new VerifyReq(kase.Id, Copy(Current()), Intent.Confirm, null, null);
        await Session.Run(async () =>
        {
            var v = await Session.Service.VerifyInvoice(req, Ct);
            Apply(v);
            if (!v.Accepted || v.Case is null)
            {
                Session.Fail("Nicht bestätigt, bitte die markierten Werte korrigieren.");
                return;
            }
            model.Dirty = false;
            onSaved(v.Case);
            (TopLevel.GetTopLevel(this) as InvoiceWindow)?.Close();
        });
        model.Saving = false;
    }

    async void ExportCsv(object? sender, RoutedEventArgs e)
    {
        if (Session.Case is null) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.ExportInvoice(Session.Case.Id, invoice.Id, Ct);
            await Session.SaveFile(resp.FileName, resp.Data, Session.CsvFilter);
        });
    }

    void AddLine(object? sender, RoutedEventArgs e)
    {
        var no = model.Lines.Count == 0 ? 0 : model.Lines.Max(r => r.Line.No);
        var row = new LineRow(new InvoiceLine { No = no + 1, PriceBaseQty = 1000 }, Math.Max(currentPage, 0), []);
        row.Changed += LineEdited;
        model.Lines.Add(row);
        Schedule();
    }

    void RemoveLine(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not LineRow row) return;
        row.Changed -= LineEdited;
        model.Lines.Remove(row);
        Schedule();
    }

    void CellChanged(object? sender, EventArgs e)
    {
        if (Lines.SelectedItem is not LineRow row || Lines.CurrentColumn is not { } column || column.DisplayIndex >= LineFields.Length)
        {
            FocusCell(currentPage, null);
            return;
        }
        var (page, box) = Where(row, LineFields[column.DisplayIndex]);
        FocusCell(page, box);
    }

    void TotalChanged(object? sender, EventArgs e)
    {
        if (Totals.SelectedItem is not TotalRow row) return;
        var (page, box) = Stated(row.Field);
        FocusCell(page, box);
    }

    // A template cell leaves focus on the cell itself; typing should land in its box right away.
    void EditStarted(object? sender, DataGridPreparingCellForEditEventArgs e)
    {
        if (e.EditingElement is not TextBox box) return;
        box.Focus();
        box.SelectAll();
    }

    // A value the row does not print itself, like the one rate the totals state, was read once
    // for the whole document.
    (int Page, Box? Box) Where(LineRow row, Field field) =>
        row.Cells.TryGetValue(field, out var cell) ? (row.Page, cell.Box)
        : Stated(field) is { Box: not null } stated ? stated
        : (row.Page, null);

    (int Page, Box? Box) Stated(Field field)
    {
        for (var i = 0; i < pages.Count; i++)
            if (pages[i].Header.TryGetValue(field, out var word)) return (i, word.Box);
        return (currentPage, null);
    }

    void HeaderFocus(object? sender, FocusChangedEventArgs e)
    {
        var field = ReferenceEquals(sender, SupplierBox) ? Field.Supplier
            : ReferenceEquals(sender, NumberBox) ? Field.InvoiceNumber
            : Field.InvoiceDate;
        var (page, box) = Stated(field);
        FocusCell(page, box);
    }

    void PageChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PageSelect.SelectedIndex >= 0) ShowPage(PageSelect.SelectedIndex);
    }

    void FocusCell(int page, Box? box)
    {
        if (pages.Count == 0) return;
        if (page != currentPage && page >= 0 && page < pages.Count) PageSelect.SelectedIndex = page;
        if (box is null)
        {
            FocusBox.IsVisible = false;
            return;
        }
        Canvas.SetLeft(FocusBox, box.X - 4);
        Canvas.SetTop(FocusBox, box.Y - 4);
        FocusBox.Width = box.W + 8;
        FocusBox.Height = box.H + 8;
        FocusBox.IsVisible = true;
        ZoomPan.Reveal(Viewer, new Rect(box.X - 4, box.Y - 4, box.W + 8, box.H + 8));
    }

    void ZoomIn(object? sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, ZoomPan.Step);

    void ZoomOut(object? sender, RoutedEventArgs e) => ZoomPan.ZoomBy(Viewer, 1 / ZoomPan.Step);

    void ZoomFit(object? sender, RoutedEventArgs e) => FitPage();

    void FitPage()
    {
        Viewer.UpdateLayout();
        ZoomPan.FitWidth(Viewer);
    }

    void ShowPage(int index)
    {
        if (index == currentPage || index < 0 || index >= pages.Count) return;
        currentPage = index;
        var page = pages[index];
        PageImage.Source = null;
        if (page.Width > 0 && page.Height > 0) Place(page.Width, page.Height);
        FocusBox.IsVisible = false;
        RenderFlagged();
        FitPage();
        Decode(page, index);
    }

    async void Decode(OcrPage page, int index)
    {
        if (page.Image is not { } raster) return;
        var image = await Task.Run(() => Images.From(raster));
        if (currentPage != index || pages.ElementAtOrDefault(index) != page) return;
        PageImage.Source = image;
        if (page.Width > 0 && page.Height > 0) return;
        Place(image.PixelSize.Width, image.PixelSize.Height);
        FitPage();
    }

    void Place(double w, double h)
    {
        PageCanvas.Width = w;
        PageCanvas.Height = h;
        PageImage.Width = w;
        PageImage.Height = h;
    }

    void RenderFlagged()
    {
        Flagged.Children.Clear();
        if (currentPage < 0 || currentPage >= pages.Count) return;
        var page = pages[currentPage];
        var boxes = new List<Box>();
        foreach (var f in flags.Where(f => f.LineNo == 0 && f.Field is not null))
            if (page.Header.TryGetValue(f.Field!.Value, out var cell)) boxes.Add(cell.Box);
        foreach (var row in model.Lines)
        foreach (var f in flags.Where(f => f.LineNo == row.Line.No && f.LineNo != 0 && f.Field is not null))
            if (Where(row, f.Field!.Value) is (var at, { } box) && at == currentPage) boxes.Add(box);
        foreach (var b in boxes)
        {
            var rect = new Rectangle
            {
                Width = b.W + 6,
                Height = b.H + 6,
                Classes = { "flagged" },
            };
            Canvas.SetLeft(rect, b.X - 3);
            Canvas.SetTop(rect, b.Y - 3);
            Flagged.Children.Add(rect);
        }
    }
}

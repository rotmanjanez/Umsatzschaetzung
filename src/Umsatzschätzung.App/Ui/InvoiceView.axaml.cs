using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public enum Checked { Automatic, Manual, Pending }

public static class Checks
{
    public static Checked Of(Invoice inv) =>
        inv.Verification is { } v ? (v.Auto ? Checked.Automatic : Checked.Manual)
        : inv.Source == Source.Scan ? Checked.Pending
        : Checked.Automatic;

    public static string Text(Invoice inv) =>
        inv.Verification is { } v
            ? (v.Auto ? "Automatisch geprüft am " : "Manuell geprüft am ") + Format.Timestamp(v.At)
            : inv.Source == Source.Scan ? "Prüfung offen" : "Automatisch übernommen";
}

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

public sealed class InvoiceModel : Observable
{
    string supplier = "", number = "", date = "", netTotal = "", grossTotal = "", fileName = "", stateText = "", periodHint = "";
    string? netFlag, grossFlag;
    Checked state = Checked.Pending;
    bool valid, saving, dirty, outsidePeriod;

    public string Supplier { get => supplier; set => Set(ref supplier, value); }
    public string Number { get => number; set => Set(ref number, value); }
    public string Date { get => date; set => Set(ref date, value); }
    public string NetTotal { get => netTotal; set => Set(ref netTotal, value); }
    public string GrossTotal { get => grossTotal; set => Set(ref grossTotal, value); }
    public string FileName { get => fileName; set => Set(ref fileName, value); }
    public string PeriodHint { get => periodHint; set => Set(ref periodHint, value); }
    public bool OutsidePeriod { get => outsidePeriod; set => Set(ref outsidePeriod, value); }

    public Checked State
    {
        get => state;
        set
        {
            if (!Set(ref state, value)) return;
            foreach (var p in new[] { nameof(IsAutomatic), nameof(IsManual), nameof(IsPending), nameof(SaveLabel), nameof(SaveReady) }) Raise(p);
        }
    }

    public string StateText { get => stateText; set => Set(ref stateText, value); }
    public bool IsAutomatic => state == Checked.Automatic;
    public bool IsManual => state == Checked.Manual;
    public bool IsPending => state == Checked.Pending;

    public bool Valid { get => valid; set { if (Set(ref valid, value)) Raise(nameof(SaveReady)); } }
    public bool Dirty { get => dirty; set { if (Set(ref dirty, value)) Raise(nameof(SaveReady)); } }
    public bool Saving { get => saving; set { if (Set(ref saving, value)) { Raise(nameof(SaveReady)); Raise(nameof(SaveLabel)); } } }

    public bool SaveReady => valid && !saving && (dirty || state == Checked.Pending);
    public string SaveLabel => saving ? "Wird gespeichert …" : state == Checked.Pending ? "Bestätigen" : "Änderungen speichern";

    public string? NetFlag => netFlag;
    public string? GrossFlag => grossFlag;
    public ObservableCollection<string> HeaderFlags { get; } = [];
    public ObservableCollection<LineRow> Lines { get; } = [];

    public void SetHeaderFlags(IEnumerable<Flag> flags)
    {
        netFlag = grossFlag = null;
        HeaderFlags.Clear();
        foreach (var f in flags)
        {
            HeaderFlags.Add(f.Message);
            switch (f.Field)
            {
                case Field.NetTotal: netFlag ??= f.Message; break;
                case Field.GrossTotal: grossFlag ??= f.Message; break;
            }
        }
        Raise(nameof(NetFlag));
        Raise(nameof(GrossFlag));
    }
}

// One editor for every invoice, whether it still awaits review or was taken over long ago: the
// document sits below the values, and the values it was read from light up on the document.
public partial class InvoiceView : Screen
{
    static readonly Field[] LineFields = [Field.Quantity, Field.Unit, Field.Name, Field.UnitPrice, Field.LineNet, Field.Vat];

    static readonly string[] HeaderFields =
        [nameof(InvoiceModel.Supplier), nameof(InvoiceModel.Number), nameof(InvoiceModel.Date)];

    readonly InvoiceModel model = new();
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly Action<CaseResp> onSaved;
    readonly List<OcrPage> pages;
    Invoice invoice;
    InvoiceDisplay display;
    List<Flag> flags = [];
    int currentPage = -1;
    int previewSeq;
    bool applying, sourceLoaded, checkedOnce;


    public InvoiceView(Session session, Invoice stored, InvoiceDisplay storedDisplay, OcrResp? ocr, Action<CaseResp> onSaved) : base(session)
    {
        InitializeComponent();
        this.onSaved = onSaved;
        invoice = Copy(stored);
        display = storedDisplay;
        pages = ocr?.Pages ?? [];
        DataContext = model;
        model.PropertyChanged += HeaderEdited;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _ = Preview();
        };
        Load();
        if (pages.Count > 0)
        {
            foreach (var p in pages)
            {
                flags.AddRange(p.Flags);
                foreach (var l in p.Lines) flags.AddRange(l.Flags);
            }
            for (var i = 0; i < pages.Count; i++) PageSelect.Items.Add("Seite " + (i + 1));
            PageSelect.SelectedIndex = 0;
            PageSelect.IsVisible = pages.Count > 1;
        }
        else
        {
            OcrPane.IsVisible = false;
            Source.IsVisible = true;
        }
        ApplyFlags(flags);
    }

    public string Id => invoice.Id;

    // Worth keeping around once the user leaves it: unsaved edits, or a review still to be done.
    public bool Keep => model.Dirty || model.State == Checked.Pending;

    protected override async void OnEnter()
    {
        if (!checkedOnce)
        {
            checkedOnce = true;
            await Preview();
        }
        if (pages.Count > 0 || sourceLoaded || Session.Case is null) return;
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

    // The case keeps the stored invoice; edits stay in this copy until they are saved.
    static Invoice Copy(Invoice inv) => Json.Deserialize<Invoice>(Json.Serialize(inv));

    void Load()
    {
        applying = true;
        model.Supplier = invoice.SupplierName;
        model.Number = invoice.Number;
        model.Date = display.Date;
        model.NetTotal = display.NetTotal;
        model.GrossTotal = display.GrossTotal;
        model.FileName = invoice.FileName;
        model.State = Checks.Of(invoice);
        model.StateText = Checks.Text(invoice);
        ShowPeriod();
        foreach (var row in model.Lines) row.Changed -= LineEdited;
        model.Lines.Clear();
        var refs = pages.SelectMany((p, i) => p.Lines.Select(l => (Page: i, Line: (OcrLine?)l))).ToList();
        for (var i = 0; i < invoice.Lines.Count; i++)
        {
            var d = i < display.Lines.Count ? display.Lines[i] : new LineDisplay("", "", "", "", "");
            var r = i < refs.Count ? refs[i] : (Page: Math.Max(currentPage, 0), Line: null);
            var row = new LineRow(invoice.Lines[i], d, r.Page, r.Line?.Cells ?? []);
            row.Changed += LineEdited;
            model.Lines.Add(row);
        }
        applying = false;
    }

    void ShowPeriod()
    {
        model.OutsidePeriod = Session.Case is { } k && invoice.Date is { } d && (d < k.PeriodFrom || d > k.PeriodTo);
        model.PeriodHint = "Prüfungszeitraum " + (Session.Display?.Period ?? "");
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

    void LineEdited()
    {
        if (!applying) Schedule();
    }

    void Schedule()
    {
        model.Dirty = true;
        timer.Stop();
        timer.Start();
    }

    Invoice Current()
    {
        invoice.Lines = model.Lines.Select(r => r.Line).ToList();
        return invoice;
    }

    async Task Preview()
    {
        if (Session.Case is null) return;
        var seq = ++previewSeq;
        var req = new VerifyReq(Session.Case.Id, Current(), Intent.Check, null, null);
        await Session.Run(async () =>
        {
            var v = await Session.Service.VerifyInvoice(req, Ct);
            if (seq == previewSeq) ApplyTotals(v);
        });
    }

    void ApplyTotals(VerifyResp v)
    {
        invoice.NetTotal = v.Invoice.NetTotal;
        invoice.GrossTotal = v.Invoice.GrossTotal;
        display = display with { NetTotal = v.Display.NetTotal, GrossTotal = v.Display.GrossTotal };
        applying = true;
        model.NetTotal = display.NetTotal;
        model.GrossTotal = display.GrossTotal;
        applying = false;
        ApplyFlags(v.Flags, v.Blocked);
    }

    void Apply(VerifyResp v)
    {
        invoice = Copy(v.Invoice);
        display = v.Display;
        Load();
        ApplyFlags(v.Flags, v.Blocked);
    }

    // Whether a flag blocks is the service's call: on open the reading's own flags are only drawn,
    // and the first check that comes back decides.
    void ApplyFlags(List<Flag> all, bool blocked = false)
    {
        flags = all;
        model.SetHeaderFlags(all.Where(f => f.LineNo == 0));
        foreach (var row in model.Lines) row.SetFlags(all.Where(f => f.LineNo == row.Line.No && f.LineNo != 0));
        model.Valid = !blocked;
        RenderFlagged();
    }

    async void Save(object? sender, RoutedEventArgs e)
    {
        if (Session.Case is null) return;
        timer.Stop();
        Lines.CommitEdit(DataGridEditingUnit.Row, true);
        var req = new VerifyReq(Session.Case.Id, Current(), Intent.Confirm, null, null);
        model.Saving = true;
        await Session.Run(async () =>
        {
            var v = await Session.Service.VerifyInvoice(req, Ct);
            Apply(v);
            if (!v.Accepted || v.Case is null)
            {
                Session.Fail("Nicht gespeichert, bitte die markierten Werte korrigieren.");
                return;
            }
            model.Dirty = false;
            onSaved(v.Case);
        });
        model.Saving = false;
    }

    void AddLine(object? sender, RoutedEventArgs e)
    {
        var no = model.Lines.Count == 0 ? 0 : model.Lines.Max(r => r.Line.No);
        var row = new LineRow(new InvoiceLine { No = no + 1, PriceBaseQty = 1000 }, new LineDisplay("", "", "", "", ""), Math.Max(currentPage, 0), []);
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
        FocusCell(row.Page, row.Cells.GetValueOrDefault(LineFields[column.DisplayIndex])?.Box);
    }

    void HeaderFocus(object? sender, FocusChangedEventArgs e)
    {
        if (pages.Count == 0) return;
        var field = ReferenceEquals(sender, SupplierBox) ? Field.Supplier
            : ReferenceEquals(sender, NumberBox) ? Field.InvoiceNumber
            : Field.InvoiceDate;
        FocusCell(0, pages[0].Header.GetValueOrDefault(field)?.Box);
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
        var image = Images.Decode(page.Image);
        var w = page.Width > 0 ? page.Width : image.PixelSize.Width;
        var h = page.Height > 0 ? page.Height : image.PixelSize.Height;
        PageCanvas.Width = w;
        PageCanvas.Height = h;
        PageImage.Source = image;
        PageImage.Width = w;
        PageImage.Height = h;
        FocusBox.IsVisible = false;
        RenderFlagged();
        FitPage();
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
                Classes = { "flagged" },
            };
            Canvas.SetLeft(rect, b.X - 3);
            Canvas.SetTop(rect, b.Y - 3);
            Flagged.Children.Add(rect);
        }
    }
}

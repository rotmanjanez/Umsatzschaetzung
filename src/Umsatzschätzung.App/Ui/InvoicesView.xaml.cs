using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class InvoiceRow(Invoice invoice, InvoiceDisplay display)
{
    public Invoice Invoice { get; } = invoice;
    public InvoiceDisplay Display { get; } = display;
    public string Id => Invoice.Id;
    public string Supplier => Invoice.SupplierName;
    public bool IsDraft => Invoice.Source == Source.Scan && Invoice.Verification is null;
    public string Sub => (IsDraft ? "Zu prüfen · " : "") + Invoice.Number + " · " + Display.Date + " · " + Display.NetTotal;
}

public sealed record InvoiceLineRow(string Name, string Quantity, string UnitPrice, string LineNet, string Vat, string Mapping)
{
    public bool Mapped => Mapping != "";
    public bool Unmapped => Mapping == "";
}

public sealed class InvoicesModel : Observable
{
    bool empty = true, importing;
    int importDone, importTotal;
    string importFile = "";

    public ObservableCollection<InvoiceRow> Invoices { get; } = [];
    public bool Empty { get => empty; set => Set(ref empty, value); }
    public bool Importing { get => importing; set => Set(ref importing, value); }
    public int ImportDone { get => importDone; set { if (Set(ref importDone, value)) Raise(nameof(ImportText)); } }
    public int ImportTotal { get => importTotal; set { if (Set(ref importTotal, value)) Raise(nameof(ImportText)); } }
    public string ImportFile { get => importFile; set { if (Set(ref importFile, value)) Raise(nameof(ImportText)); } }
    public string ImportText => (ImportDone + 1) + " von " + ImportTotal + " Dateien · " + ImportFile;
}

public partial class InvoicesView : Screen
{
    readonly InvoicesModel model = new();
    readonly Dictionary<string, InvoiceSourceResp?> sources = [];
    readonly Dictionary<string, VerifyView> editors = [];
    readonly Queue<PickedFile> queue = new();
    CancellationTokenSource? importCts;
    VerifyView? verify;
    bool refreshing;

    public InvoicesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Session.CaseChanged += Refresh;
        Session.CaseClosed += CaseClosed;
    }

    protected override void OnEnter()
    {
        Refresh();
        verify?.Enter();
    }

    protected override void OnLeave() => verify?.Leave();

    void CaseClosed()
    {
        importCts?.Cancel();
        queue.Clear();
        sources.Clear();
        foreach (var e in editors.Values) e.Leave();
        editors.Clear();
        SwapVerify(null);
    }

    void Refresh()
    {
        if (!IsActive || refreshing) return;
        refreshing = true;
        var selected = (List.SelectedItem as InvoiceRow)?.Id;
        model.Invoices.Clear();
        if (Session.Case is { } k && Session.Display is { } d)
            foreach (var inv in k.Invoices)
                model.Invoices.Add(new InvoiceRow(inv, d.Invoices.GetValueOrDefault(inv.Id) ?? new InvoiceDisplay("", "", "", [])));
        model.Empty = model.Invoices.Count == 0;
        List.SelectedItem = model.Invoices.FirstOrDefault(r => r.Id == selected);
        refreshing = false;
        ShowDetail(List.SelectedItem as InvoiceRow);
    }

    void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) ShowDetail(List.SelectedItem as InvoiceRow);
    }

    void ShowDetail(InvoiceRow? row)
    {
        if (row is null)
        {
            SwapVerify(null);
            Detail.Visibility = Visibility.Collapsed;
            EmptyDetail.Visibility = Visibility.Visible;
            return;
        }
        EmptyDetail.Visibility = Visibility.Collapsed;
        if (row.IsDraft)
        {
            Detail.Visibility = Visibility.Collapsed;
            SwapVerify(EditorFor(row));
            return;
        }
        SwapVerify(null);
        Supplier.Text = row.Supplier;
        Number.Text = row.Invoice.Number;
        Date.Text = row.Display.Date;
        Net.Text = row.Display.NetTotal;
        Gross.Text = row.Display.GrossTotal;
        LinesGrid.ItemsSource = row.Invoice.Lines.Select((l, i) =>
        {
            var d = i < row.Display.Lines.Count ? row.Display.Lines[i] : new LineDisplay("", "", "", "", "");
            return new InvoiceLineRow(l.Name, d.Quantity, d.UnitPrice, d.LineNet, d.Vat, d.Mapping);
        }).ToList();
        Detail.Visibility = Visibility.Visible;
        _ = LoadSource(row.Id);
    }

    void SwapVerify(VerifyView? next)
    {
        if (verify == next) return;
        verify?.Leave();
        verify = next;
        VerifyHost.Content = next;
        VerifyHost.Visibility = next is null ? Visibility.Collapsed : Visibility.Visible;
        if (next is not null && IsActive) next.Enter();
    }

    VerifyView EditorFor(InvoiceRow row)
    {
        if (editors.TryGetValue(row.Id, out var existing)) return existing;
        var editor = new VerifyView(Session, row.Invoice, row.Display, Session.Drafts.GetValueOrDefault(row.Id), stored =>
        {
            editors.Remove(row.Id);
            SwapVerify(null);
            Session.SetCase(stored);
        });
        editors[row.Id] = editor;
        return editor;
    }

    async Task LoadSource(string id)
    {
        if (Session.Case is null) return;
        if (sources.TryGetValue(id, out var cached))
        {
            Source.Show(cached, cached?.FileName ?? "Kein Beleg gespeichert.");
            return;
        }
        var caseId = Session.Case.Id;
        Source.Show(null, "Beleg wird geladen …");
        InvoiceSourceResp? resp = null;
        await Session.Run(async () => resp = await Session.Service.InvoiceSource(caseId, id, Ct));
        if (Ct.IsCancellationRequested) return;
        sources[id] = resp;
        if ((List.SelectedItem as InvoiceRow)?.Id == id) Source.Show(resp, resp?.FileName ?? "Kein Beleg gespeichert.");
    }

    async void AddFiles(object sender, RoutedEventArgs e)
    {
        var files = await Session.PickFiles(Session.InvoiceFilter, true);
        StartImport(files);
    }

    void DragOverFiles(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    async void Dropped(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        StartImport(await Session.ReadFiles(paths));
    }

    void AbortImport(object sender, RoutedEventArgs e) => importCts?.Cancel();

    void StartImport(List<PickedFile> files)
    {
        if (files.Count == 0 || Session.Case is null) return;
        foreach (var f in files) queue.Enqueue(f);
        model.ImportTotal += files.Count;
        if (importCts is null) _ = RunImport(Session.Case.Id);
    }

    async Task RunImport(string caseId)
    {
        importCts = new CancellationTokenSource();
        var ct = importCts.Token;
        model.ImportDone = 0;
        model.Importing = true;
        var stored = 0;
        var drafts = 0;
        var failed = new List<string>();
        string? firstDraft = null;
        while (queue.Count > 0 && !ct.IsCancellationRequested)
        {
            var file = queue.Dequeue();
            model.ImportFile = file.Name;
            try
            {
                var parsed = await Session.Service.ParseInvoice(caseId, file.Name, file.Data, ct);
                if (!parsed.NeedsOcr)
                {
                    if (parsed.Case is not null && Session.Case?.Id == caseId) Session.SetCase(parsed.Case);
                    stored++;
                }
                else
                {
                    var ocr = await Session.Service.OcrInvoice(caseId, file.Name, file.Data, ct);
                    var v = await Session.Service.VerifyInvoice(new VerifyReq(caseId, ocr.Draft, false, true, file.Name, file.Data), ct);
                    Session.Drafts[v.Invoice.Id] = ocr;
                    if (v.Case is not null && Session.Case?.Id == caseId) Session.SetCase(v.Case);
                    drafts++;
                    firstDraft ??= v.Invoice.Id;
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ServiceError ex)
            {
                failed.Add(file.Name + ": " + ex.Message);
            }
            model.ImportDone++;
        }
        queue.Clear();
        importCts.Dispose();
        importCts = null;
        model.Importing = false;
        model.ImportTotal = 0;
        var summary = stored + " Rechnungen übernommen, " + drafts + " zur Prüfung";
        Session.Message = summary;
        if (failed.Count > 0) Session.Fail("Nicht importiert: " + string.Join("; ", failed));
        if (firstDraft is not null && IsActive)
            List.SelectedItem = model.Invoices.FirstOrDefault(r => r.Id == firstDraft);
    }
}

using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
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
    bool empty = true;
    bool dragging;

    public ObservableCollection<InvoiceRow> Invoices { get; } = [];

    public bool Empty
    {
        get => empty;
        set { if (Set(ref empty, value)) Raise(nameof(DropHint)); }
    }

    public bool Dragging
    {
        get => dragging;
        set { if (Set(ref dragging, value)) Raise(nameof(DropHint)); }
    }

    public bool DropHint => dragging && !empty;
}

public partial class InvoicesView : Screen
{
    readonly InvoicesModel model = new();
    readonly Dictionary<string, InvoiceSourceResp?> sources = [];
    readonly Dictionary<string, VerifyView> editors = [];
    VerifyView? verify;
    bool refreshing;

    public InvoicesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Search.Attach(model.Invoices, r => r.Supplier + " " + r.Invoice.Number + " " + r.Display.Date + " " + r.Display.NetTotal);
        List.ItemsSource = Search.View;
        AddHandler(DragDrop.DragOverEvent, DragOverFiles);
        AddHandler(DragDrop.DragLeaveEvent, DragLeft);
        AddHandler(DragDrop.DropEvent, Dropped);
        Session.CaseChanged += Refresh;
        Session.CaseClosed += CaseClosed;
        Session.Imports.Finished += ImportFinished;
    }

    protected override void OnEnter()
    {
        Refresh();
        verify?.Enter();
    }

    protected override void OnLeave() => verify?.Leave();

    void CaseClosed()
    {
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

    void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) ShowDetail(List.SelectedItem as InvoiceRow);
    }

    void ShowDetail(InvoiceRow? row)
    {
        if (row is null)
        {
            SwapVerify(null);
            Detail.IsVisible = false;
            EmptyDetail.IsVisible = !model.Empty;
            return;
        }
        EmptyDetail.IsVisible = false;
        if (row.IsDraft)
        {
            Detail.IsVisible = false;
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
        Detail.IsVisible = true;
        _ = LoadSource(row.Id);
    }

    void SwapVerify(VerifyView? next)
    {
        if (verify == next) return;
        verify?.Leave();
        verify = next;
        VerifyHost.Content = next;
        VerifyHost.IsVisible = next is not null;
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

    async void Delete(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not InvoiceRow row || Session.Case is not { } k) return;
        var answer = await Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "Die Rechnung „" + row.Supplier + " · " + row.Invoice.Number + "“ wird mit dem Beleg unwiderruflich gelöscht.",
            "Rechnung löschen");
        if (!answer) return;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.DeleteInvoice(k.Id, row.Id, Ct);
            Forget(row.Id);
            Session.SetCase(resp);
        });
    }

    void Forget(string id)
    {
        sources.Remove(id);
        Session.Drafts.Remove(id);
        if (!editors.Remove(id, out var editor)) return;
        if (verify == editor) SwapVerify(null);
        else editor.Leave();
    }

    async void AddFiles(object? sender, RoutedEventArgs e)
    {
        StartImport(await Session.PickFiles(Session.InvoiceFilter, true));
    }

    void DragOverFiles(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = files ? DragDropEffects.Copy : DragDropEffects.None;
        model.Dragging = files;
        e.Handled = true;
    }

    void DragLeft(object? sender, RoutedEventArgs e) => model.Dragging = false;

    async void Dropped(object? sender, DragEventArgs e)
    {
        model.Dragging = false;
        if (e.DataTransfer.TryGetFiles() is not { } items) return;
        StartImport(await Session.ReadFiles(items.Select(f => f.TryGetLocalPath()).OfType<string>()));
    }

    void StartImport(List<PickedFile> files)
    {
        if (Session.Case is { } k) Session.Imports.Add(k.Id, k.Label, files);
    }

    void ImportFinished(ImportJob job)
    {
        if (job.FirstDraft is not { } id || Session.Case?.Id != job.CaseId || !IsActive) return;
        List.SelectedItem = model.Invoices.FirstOrDefault(r => r.Id == id);
    }
}

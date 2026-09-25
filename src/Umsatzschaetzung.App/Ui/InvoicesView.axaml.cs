using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Platform.Storage;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed class InvoiceRow(Invoice invoice)
{
    public Invoice Invoice { get; set; } = invoice;
    public string Id { get; } = invoice.Id;
    public string Supplier { get; } = invoice.SupplierName;
    public string Number { get; } = invoice.Number;
    public string Date { get; } = Format.Date(invoice.Date);
    public string NetTotal { get; } = Format.Cents(invoice.NetTotal);
    public string FileName { get; } = invoice.FileName;
    public Checked State { get; } = Checks.Of(invoice);
    public bool IsAutomatic => State == Checked.Automatic;
    public bool IsManual => State == Checked.Manual;
    public bool IsPending => State == Checked.Pending;
    public string StateText => State switch
    {
        Checked.Automatic => "Automatisch",
        Checked.Manual => "Manuell",
        _ => "Durchsicht offen",
    };

    // What the three sortable columns sort by: the review still to be done comes first.
    public int Rank => State == Checked.Pending ? 0 : State == Checked.Manual ? 1 : 2;
    public DateOnly Sort { get; } = invoice.Date ?? DateOnly.MinValue;
    public long Net { get; } = invoice.NetTotal;

    public bool Shows(InvoiceRow other) =>
        (Supplier, Number, Date, NetTotal, FileName, State, Sort, Net)
        == (other.Supplier, other.Number, other.Date, other.NetTotal, other.FileName, other.State, other.Sort, other.Net);
}

public sealed class InvoicesModel : Observable
{
    string summary = "";
    bool empty = true;
    bool dragging;

    public ObservableCollection<InvoiceRow> Invoices { get; } = [];

    public string Summary
    {
        get => summary;
        set => Set(ref summary, value);
    }

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

    public void Counted()
    {
        Empty = Invoices.Count == 0;
        Summary = Empty ? "" : Count(Checked.Pending) + " offen, " + Count(Checked.Automatic) + " automatisch, " + Count(Checked.Manual) + " manuell";
    }

    int Count(Checked state) => Invoices.Count(r => r.State == state);
}

public partial class InvoicesView : Screen
{
    public override string Topic => Help.Invoices;

    protected override int Page => (int)Tab.Invoices;

    readonly InvoicesModel model = new();
    readonly Dictionary<string, InvoiceView> editors = [];
    readonly Dictionary<string, InvoiceWindow> windows = [];
    bool refreshing;

    public InvoicesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        var view = Search.Attach(model.Invoices, Text);
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(InvoiceRow.Rank)));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(InvoiceRow.Supplier)));
        List.ItemsSource = view;
        AddHandler(DragDrop.DragOverEvent, DragOverFiles);
        AddHandler(DragDrop.DragLeaveEvent, DragLeft);
        AddHandler(DragDrop.DropEvent, Dropped);
        Session.CaseChanged += Refresh;
        Session.CaseClosed += CaseClosed;
        Session.InvoiceRequested += id => { if (Session.Case?.Invoices.Find(i => i.Id == id) is { } inv) Show(new InvoiceRow(inv)); };
    }

    static string Text(InvoiceRow r) => r.Supplier + " " + r.Number + " " + r.Date + " " + r.NetTotal + " " + r.FileName + " " + r.StateText;

    protected override void OnEnter()
    {
        Refresh();
        if (Revealing() is { } id) Reveal.Row(List, model.Invoices.FirstOrDefault(r => r.Id == id));
    }

    void CaseClosed()
    {
        Session.Sources.Clear();
        foreach (var w in windows.Values.ToList()) w.Close();
        foreach (var e in editors.Values) e.Leave();
        editors.Clear();
    }

    void Refresh()
    {
        if (!IsActive || refreshing) return;
        refreshing = true;
        var selected = (List.SelectedItem as InvoiceRow)?.Id;
        Merge(Session.Case?.Invoices ?? []);
        model.Counted();
        List.SelectedItem = model.Invoices.FirstOrDefault(r => r.Id == selected);
        refreshing = false;
    }

    // Rows are changed one by one instead of all at once: a reset of the list scrolls it back to the top.
    void Merge(List<Invoice> invoices)
    {
        var fresh = new Dictionary<string, InvoiceRow>();
        foreach (var inv in invoices) fresh[inv.Id] = new InvoiceRow(inv);
        var rows = model.Invoices;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (!fresh.Remove(rows[i].Id, out var row)) rows.RemoveAt(i);
            else if (row.Shows(rows[i])) rows[i].Invoice = row.Invoice;
            else rows[i] = row;
        }
        foreach (var row in fresh.Values) rows.Add(row);
    }

    void RowOpened(object? sender, TappedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<DataGridRow>(true) is not null) OpenSelected();
    }

    void ListKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        OpenSelected();
        e.Handled = true;
    }

    void Open(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is InvoiceRow row) Show(row);
    }

    void OpenSelected()
    {
        if (List.SelectedItem is InvoiceRow row) Show(row);
    }

    // One window per invoice: the lines and the scan both want the room, and a second invoice can
    // be opened next to the first one to compare.
    void Show(InvoiceRow row)
    {
        if (windows.TryGetValue(row.Id, out var open))
        {
            open.Activate();
            return;
        }
        var window = new InvoiceWindow(EditorFor(row), row.Supplier + " · " + row.Number);
        windows[row.Id] = window;
        window.Closed += (_, _) => Closed(row.Id);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }

    void Closed(string id)
    {
        if (!windows.Remove(id) || !editors.TryGetValue(id, out var view)) return;
        view.Leave();
        // Only reviews in progress stay cached; a read invoice would just hold on to its page images.
        if (!view.Keep) editors.Remove(id);
    }

    InvoiceView EditorFor(InvoiceRow row)
    {
        if (editors.TryGetValue(row.Id, out var existing)) return existing;
        var view = new InvoiceView(Session, row.Invoice, Session.Readings.GetValueOrDefault(row.Id), Session.SetCase);
        editors[row.Id] = view;
        return view;
    }

    async void Delete(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is not InvoiceRow row || Session.Case is not { } k) return;
        var answer = await Dialog.Confirm(TopLevel.GetTopLevel(this) as Window,
            "Die Rechnung „" + row.Supplier + " · " + row.Number + "“ wird mit dem Beleg gelöscht. "
                + "Rückgängig machen lässt sich das, bis das Programm beendet wird.",
            "Rechnung löschen");
        if (!answer || Session.Case != k) return;
        k.Invoices.RemoveAll(i => i.Id == row.Id);
        Forget(row.Id);
        Refresh();
        await Session.SaveCase(At(row.Id), Ct);
    }

    void Forget(string id)
    {
        Session.Readings.Remove(id);
        Session.Sources.Remove(id);
        if (windows.Remove(id, out var window)) window.Close();
        if (editors.Remove(id, out var gone)) gone.Leave();
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
}

using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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
    public Checked State => Checks.Of(Invoice);
    public string Sub => Invoice.Number + " · " + Display.Date + " · " + Display.NetTotal;
}

public sealed class InvoicesModel : Observable
{
    bool empty = true;
    bool dragging;
    bool selected;

    public ObservableCollection<InvoiceRow> Automatic { get; } = [];
    public ObservableCollection<InvoiceRow> Manual { get; } = [];
    public ObservableCollection<InvoiceRow> Pending { get; } = [];

    public bool HasAutomatic => Automatic.Count > 0;
    public bool HasManual => Manual.Count > 0;
    public bool HasPending => Pending.Count > 0;

    public bool Empty
    {
        get => empty;
        set => Set(ref empty, value);
    }

    public bool Dragging
    {
        get => dragging;
        set { if (Set(ref dragging, value)) Raise(nameof(DropHint)); }
    }

    public bool Selected
    {
        get => selected;
        set { if (Set(ref selected, value)) { Raise(nameof(Idle)); Raise(nameof(DropHint)); } }
    }

    public bool Idle => !selected;

    public bool DropHint => dragging && selected;

    public void Sections()
    {
        Empty = Automatic.Count + Manual.Count + Pending.Count == 0;
        foreach (var p in new[] { nameof(HasAutomatic), nameof(HasManual), nameof(HasPending) }) Raise(p);
    }
}

public partial class InvoicesView : Screen
{
    readonly InvoicesModel model = new();
    readonly Dictionary<string, InvoiceView> editors = [];
    readonly ListBox[] lists;
    InvoiceView? editor;
    bool refreshing;

    public InvoicesView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        lists = [PendingList, AutomaticList, ManualList];
        AutomaticList.ItemsSource = Search.Attach(model.Automatic, Text);
        ManualList.ItemsSource = Search.Attach(model.Manual, Text);
        PendingList.ItemsSource = Search.Attach(model.Pending, Text);
        AddHandler(DragDrop.DragOverEvent, DragOverFiles);
        AddHandler(DragDrop.DragLeaveEvent, DragLeft);
        AddHandler(DragDrop.DropEvent, Dropped);
        Session.CaseChanged += Refresh;
        Session.CaseClosed += CaseClosed;
        Session.Imports.Finished += ImportFinished;
    }

    static string Text(InvoiceRow r) => r.Supplier + " " + r.Invoice.Number + " " + r.Display.Date + " " + r.Display.NetTotal;

    protected override void OnEnter()
    {
        Refresh();
        editor?.Enter();
    }

    protected override void OnLeave() => editor?.Leave();

    void CaseClosed()
    {
        Session.Sources.Clear();
        foreach (var e in editors.Values) e.Leave();
        editors.Clear();
        Swap(null);
    }

    void Refresh()
    {
        if (!IsActive || refreshing) return;
        refreshing = true;
        var selected = Selected()?.Id;
        foreach (var c in new[] { model.Automatic, model.Manual, model.Pending }) c.Clear();
        if (Session.Case is { } k && Session.Display is { } d)
            foreach (var inv in k.Invoices)
            {
                var row = new InvoiceRow(inv, d.Invoices.GetValueOrDefault(inv.Id) ?? new InvoiceDisplay("", "", "", []));
                Section(row.State).Add(row);
            }
        model.Sections();
        foreach (var l in lists) l.SelectedItem = null;
        var current = Rows().FirstOrDefault(r => r.Id == selected);
        if (current is not null) List(current.State).SelectedItem = current;
        refreshing = false;
        ShowEditor(current);
    }

    IEnumerable<InvoiceRow> Rows() => model.Automatic.Concat(model.Manual).Concat(model.Pending);

    ObservableCollection<InvoiceRow> Section(Checked state) => state switch
    {
        Checked.Automatic => model.Automatic,
        Checked.Manual => model.Manual,
        _ => model.Pending,
    };

    ListBox List(Checked state) => state switch
    {
        Checked.Automatic => AutomaticList,
        Checked.Manual => ManualList,
        _ => PendingList,
    };

    InvoiceRow? Selected() => lists.Select(l => l.SelectedItem).OfType<InvoiceRow>().FirstOrDefault();

    void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshing || sender is not ListBox list || list.SelectedItem is not InvoiceRow row) return;
        foreach (var other in lists.Where(l => l != list)) other.SelectedItem = null;
        ShowEditor(row);
    }

    void ListPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((e.Source as Visual)?.FindAncestorOfType<ListBoxItem>(true) is not null) return;
        foreach (var l in lists) l.SelectedItem = null;
        ShowEditor(null);
    }

    void ShowEditor(InvoiceRow? row)
    {
        model.Selected = row is not null;
        Swap(row is null ? null : EditorFor(row));
    }

    void Swap(InvoiceView? next)
    {
        if (editor == next) return;
        editor?.Leave();
        // Only reviews in progress stay cached; a read invoice would just hold on to its page images.
        if (editor is { Keep: false }) editors.Remove(editor.Id);
        editor = next;
        EditorHost.Content = next;
        EditorHost.IsVisible = next is not null;
        if (next is not null && IsActive) next.Enter();
    }

    InvoiceView EditorFor(InvoiceRow row)
    {
        if (editors.TryGetValue(row.Id, out var existing)) return existing;
        var view = new InvoiceView(Session, row.Invoice, row.Display, Session.Drafts.GetValueOrDefault(row.Id), Session.SetCase);
        editors[row.Id] = view;
        return view;
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
        Session.Drafts.Remove(id);
        Session.Sources.Remove(id);
        if (!editors.Remove(id, out var gone)) return;
        if (editor == gone) Swap(null);
        else gone.Leave();
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
        if (Rows().FirstOrDefault(r => r.Id == id) is { } row) List(row.State).SelectedItem = row;
    }
}

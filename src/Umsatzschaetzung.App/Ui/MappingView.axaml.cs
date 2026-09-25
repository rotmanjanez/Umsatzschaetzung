using System.Collections.ObjectModel;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed class MappingModel : Observable
{
    bool noInvoices, mapping;
    string summary = "";

    public ObservableCollection<LineGroup> Groups { get; } = [];
    public string Summary { get => summary; set => Set(ref summary, value); }
    public bool NoInvoices { get => noInvoices; set => Set(ref noInvoices, value); }
    public bool Mapping { get => mapping; set => Set(ref mapping, value); }

    (Case?, RuleSet?) caughtUp;
    Task running = Task.CompletedTask;

    // What the matcher is sure about it maps on its own; the list then shows the rest.
    // A run a visit left may still be finishing its line; the next visit waits for it.
    public async Task MapOpen(Session session, CancellationToken ct)
    {
        while (!running.IsCompleted) await running;
        if (ct.IsCancellationRequested || session.Case is not { } k || session.Rules is not { } rs || !Groups.Any(g => g.IsPending)
            || caughtUp == (k, rs) || k.MappedAt == rs.Version) return;
        running = Map(session, k, ct);
        await running;
    }

    async Task Map(Session session, Case k, CancellationToken ct)
    {
        Mapping = true;
        Case? mapped = null;
        await session.Run(async () => mapped = await session.Service.MapCase(k.Id, ct));
        Mapping = false;
        if (mapped is null || session.Case != k || !await session.LoadRules(ct)) return;
        session.SetCase(mapped);
        caughtUp = (session.Case, session.Rules);
    }

    public void Counted()
    {
        int Count(Checked state) => Groups.Count(g => g.State == state);
        var open = Count(Checked.Pending);
        Summary = Groups.Count == 0 ? ""
            : (open == 0 ? "Alles zugeordnet" : open + " offen") + ", " + Count(Checked.Automatic) + " automatisch, " + Count(Checked.Manual) + " manuell";
    }
}

public partial class MappingView : Screen
{
    public override string Topic => Help.Mapping;

    protected override int Page => (int)Tab.Mapping;

    readonly MappingModel model = new();
    bool refreshing;
    int refreshes;

    public MappingView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(MappingModel.Mapping)) Detail.Blocked = model.Mapping; };
        Detail.Attach(session, () => Ct, key => At(key));
        Detail.Assigned += _ => MapOpen();
        var view = Search.Attach(model.Groups, g => g.Search);
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(LineGroup.Rank)));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(LineGroup.Supplier)));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(LineGroup.Name)));
        Groups.ItemsSource = view;
        Session.CaseChanged += () => { if (IsActive) Reload(); };
    }

    protected override void OnEnter() => Reload(catchUp: true);

    protected override void Render(RuleSet rules) => _ = Refresh();

    // Mappings the service made on its own, at import, on verification or in the
    // catch-up below, are in the store but not yet in this session's rules; the list
    // can only name them after a reload.
    async void Reload(bool catchUp = false)
    {
        var item = Revealing();
        if (Stale() && !await Session.LoadRules(Ct)) return;
        await Refresh();
        if (item is not null) Reveal.Row(Groups, model.Groups.FirstOrDefault(g => g.Key == item));
        if (catchUp) MapOpen();
    }

    bool Stale() =>
        Session.Case is { } k && k.Invoices.SelectMany(i => i.Lines)
            .Any(l => !string.IsNullOrEmpty(l.MappingId) && Session.Rules?.Mappings.ContainsKey(l.MappingId) != true);

    async void MapOpen() => await model.MapOpen(Session, Ct);

    // The open position stays open, and its detail untouched unless the refresh changed its mapping.
    // One that changed state moved away in the list, so the next open position takes its place;
    // the hint about what was just assigned stays until another position is picked.
    async Task Refresh()
    {
        var at = ++refreshes;
        var (kase, rs) = (Session.Case, Session.Rules);
        var groups = await Task.Run(() => LineGroup.Of(kase, rs));
        if (at != refreshes) return;
        var kept = Groups.SelectedItem as LineGroup;
        var focused = Groups.IsKeyboardFocusWithin;
        Detail.Refresh();
        refreshing = true;
        model.Groups.Clear();
        foreach (var g in groups) model.Groups.Add(g);
        var again = kept is null ? null : model.Groups.FirstOrDefault(g => g.Key == kept.Key);
        var moved = again is not null && again.State != kept!.State;
        if (moved) again = ((DataGridCollectionView)Groups.ItemsSource).Cast<LineGroup>().FirstOrDefault(g => g.IsPending);
        if (again is not null) Groups.SelectedItem = again;
        refreshing = false;
        model.NoInvoices = Session.Case?.Invoices.Count == 0;
        model.Counted();
        if (again is null)
        {
            Detail.Show(null);
            return;
        }
        if (moved || again.MappingId != kept!.MappingId) Detail.Show(again);
        Groups.ScrollIntoView(again, null);
        if (focused) Groups.Focus();
    }

    void GroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) Detail.Pick(Groups.SelectedItem as LineGroup);
    }

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);
}

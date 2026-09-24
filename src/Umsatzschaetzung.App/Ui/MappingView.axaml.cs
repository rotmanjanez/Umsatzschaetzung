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

    public void Counted()
    {
        int Count(Checked state) => Groups.Count(g => g.State == state);
        var open = Count(Checked.Pending);
        Summary = Groups.Count == 0 ? ""
            : (open == 0 ? "Alles zugeordnet" : open + " offen") + " · " + Count(Checked.Automatic) + " automatisch · " + Count(Checked.Manual) + " manuell";
    }
}

public partial class MappingView : Screen
{
    public override string Topic => Help.Mapping;

    readonly MappingModel model = new();
    bool refreshing;
    (Case?, RuleSet?) caughtUp;

    public MappingView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Detail.Attach(session, () => Ct);
        Detail.Assigned += _ => MapOpen();
        var view = Search.Attach(model.Groups, g => g.Supplier + " " + g.Name + " " + g.Article + " " + g.StateText);
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(LineGroup.Rank)));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(LineGroup.Supplier)));
        view.SortDescriptions.Add(DataGridSortDescription.FromPath(nameof(LineGroup.Name)));
        Groups.ItemsSource = view;
        Session.CaseChanged += () => { if (IsActive) Reload(); };
    }

    protected override void OnEnter() => Reload(catchUp: true);

    // Mappings the service made on its own, at import, on verification or in the
    // catch-up below, are in the store but not yet in this session's rules; the list
    // can only name them after a reload.
    async void Reload(bool catchUp = false)
    {
        if (Stale() && !await Session.LoadRules(Ct)) return;
        Refresh();
        if (catchUp) MapOpen();
    }

    bool Stale() =>
        Session.Case is { } k && k.Invoices.SelectMany(i => i.Lines)
            .Any(l => !string.IsNullOrEmpty(l.MappingId) && Session.Rules?.Mappings.ContainsKey(l.MappingId) != true);

    // What the matcher is sure about it maps on its own; the list then shows the rest.
    async void MapOpen()
    {
        if (Session.Case is not { } k || Session.Rules is not { } rs || model.Mapping || !model.Groups.Any(g => g.IsPending)
            || caughtUp == (k, rs) || k.MappedAt == rs.Version) return;
        Detail.Blocked = model.Mapping = true;
        Case? mapped = null;
        await Session.Run(async () => mapped = await Session.Service.MapCase(k.Id, Ct));
        Detail.Blocked = model.Mapping = false;
        if (mapped is null || Session.Case != k || !await Session.LoadRules(Ct)) return;
        Session.SetCase(mapped);
        caughtUp = (Session.Case, Session.Rules);
    }

    // The open position stays open, and its detail untouched unless the refresh changed its mapping.
    void Refresh()
    {
        var kept = Groups.SelectedItem as LineGroup;
        var focused = Groups.IsKeyboardFocusWithin;
        Detail.Refresh();
        refreshing = true;
        model.Groups.Clear();
        foreach (var g in LineGroup.Of(Session.Case, Session.Rules)) model.Groups.Add(g);
        var again = kept is null ? null : model.Groups.FirstOrDefault(g => g.Key == kept.Key);
        if (again is not null && again.State == kept!.State && again.MappingId == kept.MappingId) Groups.SelectedItem = again;
        refreshing = false;
        model.NoInvoices = Session.Case?.Invoices.Count == 0;
        model.Counted();
        if (again is null)
        {
            Detail.Reset();
            return;
        }
        if (Groups.SelectedItem != again) Groups.SelectedItem = again;
        Groups.ScrollIntoView(again, null);
        if (focused) Groups.Focus();
    }

    void GroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (!refreshing) Detail.Show(Groups.SelectedItem as LineGroup);
    }

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);
}

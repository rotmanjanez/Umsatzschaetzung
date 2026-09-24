using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public enum Exclusion { Unmapped, NoFactor, Unused }

public sealed class ExcludedRow(LineGroup group, Exclusion why, string? ingredientId, string ingredient)
{
    public LineGroup Group { get; } = group;
    public Exclusion Why { get; } = why;
    public string? IngredientId { get; } = ingredientId;
    public string Name => Group.Name;
    public string Ingredient { get; } = ingredient;
    public long Net { get; set; }
    public string LineNet => Format.Cents(Net);
}

public sealed record Exclusions(string Omitted, string Unused, string Deposit, List<ExcludedRow> Rows);

public sealed class ReportModel : Observable
{
    string omittedSummary = "", unusedSummary = "", note = "", why = "", whyTitle = "", deposit = "";
    bool hasOmitted, hasUnused, ready, busy;
    string saved = "";

    public ObservableCollection<ExcludedRow> Omitted { get; } = [];
    public ObservableCollection<ExcludedRow> Unused { get; } = [];
    public string OmittedSummary { get => omittedSummary; set => Set(ref omittedSummary, value); }
    public string UnusedSummary { get => unusedSummary; set => Set(ref unusedSummary, value); }
    public bool HasOmitted { get => hasOmitted; set { if (Set(ref hasOmitted, value)) RaiseExcluded(); } }
    public bool HasUnused { get => hasUnused; set { if (Set(ref hasUnused, value)) RaiseExcluded(); } }
    public bool HasExcluded => hasOmitted || hasUnused;
    public bool NoneExcluded => !HasExcluded;
    public string Deposit { get => deposit; set { if (Set(ref deposit, value)) { Raise(nameof(DepositBelow)); Raise(nameof(DepositAlone)); } } }
    public bool DepositBelow => deposit != "" && HasExcluded;
    public bool DepositAlone => deposit != "" && !HasExcluded;
    public string Why { get => why; set { if (Set(ref why, value)) Raise(nameof(HasWhy)); } }
    public string WhyTitle { get => whyTitle; set => Set(ref whyTitle, value); }
    public bool HasWhy => why != "";
    public bool Ready { get => ready; set { if (Set(ref ready, value)) Raise(nameof(CanSave)); } }
    public bool Busy { get => busy; set { if (Set(ref busy, value)) { Raise(nameof(CanSave)); Raise(nameof(PdfLabel)); } } }
    public bool CanSave => ready && !busy;
    public string PdfLabel => busy ? "Wird erstellt …" : "Als PDF speichern";
    public string Saved { get => saved; set { if (Set(ref saved, value)) Raise(nameof(ShowSaved)); } }
    public bool ShowSaved => saved != "";
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(ShowNote)); } }
    public bool ShowNote => note != "";

    void RaiseExcluded()
    {
        Raise(nameof(HasExcluded));
        Raise(nameof(NoneExcluded));
        Raise(nameof(DepositBelow));
        Raise(nameof(DepositAlone));
    }

    public void SetExcluded(Exclusions excluded)
    {
        OmittedSummary = excluded.Omitted;
        UnusedSummary = excluded.Unused;
        Deposit = excluded.Deposit;
        Omitted.Clear();
        Unused.Clear();
        foreach (var row in excluded.Rows) (row.Why == Exclusion.Unused ? Unused : Omitted).Add(row);
        HasOmitted = Omitted.Count > 0;
        HasUnused = Unused.Count > 0;
    }

    // One row per mapping group, as on the Zuordnung tab, so a row can be reassigned in place.
    public static Exclusions Excluded(Report r, Case c, RuleSet rs)
    {
        var s = r.Totals;
        var omitted = s.UnmappedCost == 0 ? "keine" : Format.Cents(s.UnmappedCost) + " (" + Format.Bp(s.ExcludedShare) + ")";
        var estimated = r.Estimated.Where(e => e.Source == EstimateSource.Unused).Sum(e => e.RevenueNet);
        var unused = s.UnusedCost == 0 ? "keine" : Format.Cents(s.UnusedCost) + ", geschätzter Umsatz " + Format.Cents(estimated);
        var deposit = r.Deposits.Count == 0 ? ""
            : $"Nicht in den Einkäufen: Pfand berechnet +{Format.Cents(s.DepositCharged)}, Leergut gutgeschrieben {Format.Cents(s.DepositRefunded)}";
        var at = new Dictionary<(string, long), (LineGroup Group, Invoice Invoice, InvoiceLine Line)>();
        foreach (var g in LineGroup.Of(c, rs))
            foreach (var (i, j) in g.Lines)
                at[(c.Invoices[i].Id, c.Invoices[i].Lines[j].No)] = (g, c.Invoices[i], c.Invoices[i].Lines[j]);
        var rows = new Dictionary<LineGroup, ExcludedRow>();
        void Add(string invoiceId, long no, long net, Func<LineGroup, Invoice, InvoiceLine, ExcludedRow> row)
        {
            if (!at.TryGetValue((invoiceId, no), out var hit)) return;
            if (!rows.TryGetValue(hit.Group, out var existing)) rows[hit.Group] = existing = row(hit.Group, hit.Invoice, hit.Line);
            existing.Net += net;
        }
        foreach (var l in r.Unmapped)
            Add(l.InvoiceId, l.LineNo, l.LineNet, (g, inv, line) => Match.Mapping(rs, inv.SupplierName, inv.Date, line) is { } m
                ? new ExcludedRow(g, Exclusion.NoFactor, m.IngredientId, Names.Ingredient(rs, m.IngredientId))
                : new ExcludedRow(g, Exclusion.Unmapped, null, ""));
        foreach (var l in r.Unused)
            Add(l.InvoiceId, l.LineNo, l.LineNet, (g, _, _) => new ExcludedRow(g, Exclusion.Unused, l.IngredientId, Names.Ingredient(rs, l.IngredientId)));
        return new(omitted, unused, deposit, [.. rows.Values.OrderByDescending(x => x.Net)]);
    }

    public void Explain(ExcludedRow? row)
    {
        WhyTitle = row?.Why == Exclusion.Unused ? "NICHT TEIL DER RGAS-ERMITTLUNG" : "NICHT IN DER UMSATZSCHÄTZUNG";
        Why = row?.Why switch
        {
            null => "",
            Exclusion.Unused => $"„{row.Ingredient}“ steht in keiner Rezeptur des Sortiments; der Umsatz wird über den Aufschlagsatz geschätzt",
            Exclusion.NoFactor => $"Der Zuordnung zu „{row.Ingredient}“ fehlt der Faktor",
            _ => "Keiner Zutat zugeordnet",
        };
    }
}

public partial class ReportView : Screen
{
    public override string Topic => Help.Report;

    const string Unavailable = "Berichtsvorschau nicht verfügbar, Web-Komponente konnte nicht geladen werden";

    readonly ReportModel model = new();
    bool refreshing;

    public ReportView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Detail.Attach(session, () => Ct);
        Detail.Assigned += _ => Load();
    }

    protected override void OnEnter() => Load();

    async void Load()
    {
        if (Session.Case is null) return;
        model.Ready = false;
        model.Note = "Vorschau wird erstellt …";
        await Session.LoadRules(Ct);
        if (Session.Case is not { } kase || Session.Rules is not { } rs || !IsActive) return;
        Detail.Refresh();
        await Session.Run(async () =>
        {
            var ct = Ct;
            var (excluded, html) = await Task.Run(async () =>
            {
                var calc = await Session.Service.Calculate(kase.Id, ct);
                var report = await Session.Service.RenderReport(kase.Id, false, ct);
                return (ReportModel.Excluded(calc.Report, kase, rs), report.Html);
            }, ct);
            Fill(excluded);
            await ShowHtml(html);
            model.Ready = true;
        });
        if (IsActive && model.Note == "Vorschau wird erstellt …") model.Note = "Vorschau nicht verfügbar";
    }

    // A row that is still excluded for the same mapping keeps its detail, a note on a just assigned one stays.
    void Fill(Exclusions excluded)
    {
        var kept = (OmittedGrid.SelectedItem ?? UnusedGrid.SelectedItem) as ExcludedRow;
        refreshing = true;
        model.SetExcluded(excluded);
        var again = kept is null ? null : model.Omitted.Concat(model.Unused).FirstOrDefault(r => r.Group.Key == kept.Group.Key);
        OmittedGrid.SelectedItem = again?.Why == Exclusion.Unused ? null : again;
        UnusedGrid.SelectedItem = again?.Why == Exclusion.Unused ? again : null;
        refreshing = false;
        var split = Split.RowDefinitions;
        if (split[1].Height.IsStar != model.HasExcluded)
        {
            split[1].Height = model.HasExcluded ? new GridLength(2, GridUnitType.Star) : GridLength.Auto;
            split[3].Height = new GridLength(3, GridUnitType.Star);
        }
        var lists = Lists.RowDefinitions;
        lists[1].Height = model.HasOmitted ? GridLength.Star : new GridLength(0);
        lists[3].Height = model.HasUnused ? GridLength.Star : new GridLength(0);
        model.Explain(again);
        if (again is null) Detail.Show(null);
        else if (again.Group.MappingId != kept!.Group.MappingId || again.Why != kept.Why) Detail.Show(again.Group);
    }

    void RowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshing || sender is not DataGrid grid) return;
        var row = grid.SelectedItem as ExcludedRow;
        if (row is null && (OmittedGrid.SelectedItem ?? UnusedGrid.SelectedItem) is not null) return;
        refreshing = true;
        (grid == OmittedGrid ? UnusedGrid : OmittedGrid).SelectedItem = null;
        refreshing = false;
        model.Explain(row);
        Detail.Show(row?.Group);
    }

    async Task ShowHtml(string html)
    {
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, WebViewNavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
        Web.NavigationCompleted += Completed;
        try
        {
            Web.NavigateToString(html, null!);
            model.Note = await done.Task.WaitAsync(TimeSpan.FromSeconds(20), Ct) ? "" : Unavailable;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            model.Note = Unavailable;
        }
        finally
        {
            Web.NavigationCompleted -= Completed;
        }
    }

    void ShowExcludedHelp(object? sender, RoutedEventArgs e) => Help.Open(TopLevel.GetTopLevel(this) as Window, Help.Excluded);

    async void SavePdf(object? sender, RoutedEventArgs e)
    {
        if (Session.Case is null) return;
        var caseId = Session.Case.Id;
        model.Saved = "";
        model.Busy = true;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.RenderReport(caseId, true, Ct);
            if (resp.Pdf is null)
            {
                Session.Fail("PDF konnte nicht erstellt werden");
                return;
            }
            model.Busy = false;
            if (await Session.SaveFile(resp.FileName, resp.Pdf, Session.PdfFilter) is { } name)
                model.Saved = "Gespeichert: " + name;
        });
        model.Busy = false;
    }
}

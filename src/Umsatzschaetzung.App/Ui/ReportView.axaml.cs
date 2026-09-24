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
    public string Reason => Why switch
    {
        Exclusion.Unused => "in keiner Rezeptur",
        Exclusion.NoFactor => "Faktor fehlt",
        _ => "ohne Zuordnung",
    };
    public long Net { get; set; }
    public string LineNet => Format.Cents(Net);
}

public sealed class ReportModel : Observable
{
    string summary = "", note = "", why = "", deposit = "";
    bool hasExcluded, ready, busy;
    string saved = "";

    public ObservableCollection<ExcludedRow> Rows { get; } = [];
    public string Summary { get => summary; set => Set(ref summary, value); }
    public bool HasExcluded { get => hasExcluded; set { if (Set(ref hasExcluded, value)) Raise(nameof(DepositAlone)); } }
    public string Deposit { get => deposit; set { if (Set(ref deposit, value)) { Raise(nameof(DepositBelow)); Raise(nameof(DepositAlone)); } } }
    public bool DepositBelow => deposit != "" && hasExcluded;
    public bool DepositAlone => deposit != "" && !hasExcluded;
    public string Why { get => why; set { if (Set(ref why, value)) Raise(nameof(HasWhy)); } }
    public bool HasWhy => why != "";
    public bool Ready { get => ready; set { if (Set(ref ready, value)) Raise(nameof(CanSave)); } }
    public bool Busy { get => busy; set { if (Set(ref busy, value)) { Raise(nameof(CanSave)); Raise(nameof(PdfLabel)); } } }
    public bool CanSave => ready && !busy;
    public string PdfLabel => busy ? "Wird erstellt …" : "Als PDF speichern";
    public string Saved { get => saved; set { if (Set(ref saved, value)) Raise(nameof(ShowSaved)); } }
    public bool ShowSaved => saved != "";
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(ShowNote)); } }
    public bool ShowNote => note != "";

    public void SetExcluded((string Summary, string Deposit, List<ExcludedRow> Rows) excluded)
    {
        Summary = excluded.Summary;
        Deposit = excluded.Deposit;
        Rows.Clear();
        foreach (var row in excluded.Rows) Rows.Add(row);
        HasExcluded = Rows.Count > 0;
    }

    // One row per mapping group, as on the Zuordnung tab, so a row can be reassigned in place.
    public static (string Summary, string Deposit, List<ExcludedRow> Rows) Excluded(Report r, Case c, RuleSet rs)
    {
        var s = r.Totals;
        var excluded = s.UnmappedCost + s.UnusedCost;
        var summary = excluded == 0 ? "keine" : Format.Cents(excluded) + " (" + Format.Bp(s.ExcludedShare) + ")";
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
        return (summary, deposit, [.. rows.Values.OrderByDescending(x => x.Net)]);
    }

    public void Explain(ExcludedRow? row) => Why = row?.Why switch
    {
        null => "",
        Exclusion.Unused => $"„{row.Ingredient}“ steht in keiner Rezeptur des Sortiments",
        Exclusion.NoFactor => $"Der Zuordnung zu „{row.Ingredient}“ fehlt der Faktor",
        _ => "Keiner Zutat zugeordnet",
    };
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
            Fill(excluded, kase, rs);
            await ShowHtml(html);
            model.Ready = true;
        });
        if (IsActive && model.Note == "Vorschau wird erstellt …") model.Note = "Vorschau nicht verfügbar";
    }

    // A row that is still excluded for the same mapping keeps its detail, a note on a just assigned one stays.
    void Fill((string, string, List<ExcludedRow>) excluded, Case kase, RuleSet rs)
    {
        var kept = Excluded.SelectedItem as ExcludedRow;
        refreshing = true;
        model.SetExcluded(excluded);
        var again = kept is null ? null : model.Rows.FirstOrDefault(r => r.Group.Key == kept.Group.Key);
        Excluded.SelectedItem = again;
        refreshing = false;
        Split.RowDefinitions[1].Height = model.HasExcluded ? new GridLength(2, GridUnitType.Star) : GridLength.Auto;
        model.Explain(again);
        if (again is null) Detail.Show(null);
        else if (again.Group.MappingId != kept!.Group.MappingId || again.Why != kept.Why) Detail.Show(again.Group);
    }

    void RowSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (refreshing) return;
        var row = Excluded.SelectedItem as ExcludedRow;
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

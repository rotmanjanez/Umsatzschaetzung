using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed record ExcludedLineRow(string Invoice, long LineNo, string Name, string Reason, string LineNet);

public sealed class ReportModel : Observable
{
    string purchases = "", included = "", excluded = "", note = "";
    bool hasExcluded, ready, busy;
    string saved = "";

    public ObservableCollection<ExcludedLineRow> Rows { get; } = [];
    public string Purchases { get => purchases; set => Set(ref purchases, value); }
    public string Included { get => included; set => Set(ref included, value); }
    public string Excluded { get => excluded; set => Set(ref excluded, value); }
    public bool HasExcluded { get => hasExcluded; set { if (Set(ref hasExcluded, value)) Raise(nameof(NoExcluded)); } }
    public bool NoExcluded => !hasExcluded;
    public bool Ready { get => ready; set { if (Set(ref ready, value)) Raise(nameof(CanSave)); } }
    public bool Busy { get => busy; set { if (Set(ref busy, value)) { Raise(nameof(CanSave)); Raise(nameof(PdfLabel)); } } }
    public bool CanSave => ready && !busy;
    public string PdfLabel => busy ? "Wird erstellt …" : "Als PDF speichern";
    public string Saved { get => saved; set { if (Set(ref saved, value)) Raise(nameof(ShowSaved)); } }
    public bool ShowSaved => saved != "";
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(ShowNote)); } }
    public bool ShowNote => note != "";

    public void SetExcluded(Case c, Report r, RuleSet rs)
    {
        var s = r.Totals;
        Purchases = Format.Cents(s.Purchases);
        Included = Format.Cents(s.CostOfGoods + s.StockChange);
        Excluded = Format.Cents(s.UnmappedCost + s.UnusedCost) + " (" + Format.Bp(s.ExcludedShare) + ")";
        Rows.Clear();
        foreach (var l in r.Unmapped)
            Rows.Add(new ExcludedLineRow(Names.Invoice(c, l.InvoiceId), l.LineNo, l.Name, "ohne Zuordnung", Format.Cents(l.LineNet)));
        foreach (var l in r.Unused)
            Rows.Add(new ExcludedLineRow(Names.Invoice(c, l.InvoiceId), l.LineNo, l.Name,
                "in keiner Rezeptur: " + Names.Ingredient(rs, l.IngredientId), Format.Cents(l.LineNet)));
        HasExcluded = Rows.Count > 0;
    }
}

public partial class ReportView : Screen
{
    public override string Topic => Help.Report;

    const string Unavailable = "Berichtsvorschau nicht verfügbar, Web-Komponente konnte nicht geladen werden";

    readonly ReportModel model = new();

    public ReportView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
    }

    protected override async void OnEnter()
    {
        if (Session.Case is null) return;
        model.Ready = false;
        model.Note = "Vorschau wird erstellt …";
        await Session.LoadRules(Ct);
        if (Session.Case is not { } kase || Session.Rules is not { } rs || !IsActive) return;
        await Session.Run(async () =>
        {
            var calc = await Session.Service.Calculate(kase.Id, Ct);
            var report = await Session.Service.RenderReport(kase.Id, false, Ct);
            model.SetExcluded(kase, calc.Report, rs);
            await ShowHtml(report.Html);
            model.Ready = true;
        });
        if (IsActive && model.Note == "Vorschau wird erstellt …") model.Note = "Vorschau nicht verfügbar";
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

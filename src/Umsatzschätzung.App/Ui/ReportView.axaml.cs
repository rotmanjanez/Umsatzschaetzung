using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed record ExcludedLineRow(string Invoice, long LineNo, string Name, string Reason, string LineNet);

public sealed class ReportModel : Observable
{
    string purchases = "", included = "", excluded = "", note = "";
    bool hasExcluded, ready;

    public ObservableCollection<ExcludedLineRow> Rows { get; } = [];
    public string Purchases { get => purchases; set => Set(ref purchases, value); }
    public string Included { get => included; set => Set(ref included, value); }
    public string Excluded { get => excluded; set => Set(ref excluded, value); }
    public bool HasExcluded { get => hasExcluded; set { if (Set(ref hasExcluded, value)) Raise(nameof(NoExcluded)); } }
    public bool NoExcluded => !hasExcluded;
    public bool Ready { get => ready; set => Set(ref ready, value); }
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(ShowNote)); } }
    public bool ShowNote => note != "";

    public void SetExcluded(ExcludedDisplay d)
    {
        Purchases = d.Purchases;
        Included = d.Included;
        Excluded = d.Excluded + " (" + d.Share + ")";
        Rows.Clear();
        foreach (var r in d.Unmapped) Rows.Add(new ExcludedLineRow(r.Invoice, r.LineNo, r.Name, "ohne Zuordnung", r.LineNet));
        foreach (var r in d.Unused) Rows.Add(new ExcludedLineRow(r.Invoice, r.LineNo, r.Name, "in keiner Rezeptur: " + r.Ingredient, r.LineNet));
        HasExcluded = Rows.Count > 0;
    }
}

public partial class ReportView : Screen
{
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
        var caseId = Session.Case.Id;
        model.Ready = false;
        model.Note = "Vorschau wird erstellt …";
        await Session.Run(async () =>
        {
            var calc = await Session.Service.Calculate(caseId, Ct);
            var report = await Session.Service.RenderReport(caseId, false, Ct);
            model.SetExcluded(calc.Excluded);
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
        Session.Message = "Bericht wird erstellt …";
        await Session.Run(async () =>
        {
            var resp = await Session.Service.RenderReport(caseId, true, Ct);
            if (resp.Pdf is null)
            {
                Session.Fail("PDF konnte nicht erstellt werden");
                return;
            }
            Session.Message = "";
            await Session.SaveFile(resp.FileName, resp.Pdf, Session.PdfFilter);
        });
    }

    async void ExportCsv(object? sender, RoutedEventArgs e)
    {
        if (Session.Case is null) return;
        var caseId = Session.Case.Id;
        await Session.Run(async () =>
        {
            var resp = await Session.Service.ExportCase(caseId, ExportFormat.Csv, Ct);
            await Session.SaveFile(resp.FileName, resp.Data, Session.CsvFilter);
        });
    }
}

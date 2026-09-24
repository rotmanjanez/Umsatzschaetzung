using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed class ReportModel : Observable
{
    string note = "", saved = "";
    bool ready, busy;

    public bool Ready { get => ready; set { if (Set(ref ready, value)) Raise(nameof(CanSave)); } }
    public bool Busy { get => busy; set { if (Set(ref busy, value)) Raise(nameof(CanSave)); } }
    public bool CanSave => ready && !busy;
    public string Saved { get => saved; set { if (Set(ref saved, value)) Raise(nameof(ShowSaved)); } }
    public bool ShowSaved => saved != "";
    public string Note { get => note; set { if (Set(ref note, value)) Raise(nameof(ShowNote)); } }
    public bool ShowNote => note != "";
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

    protected override void OnEnter() => Load();

    async void Load()
    {
        if (Session.Case is null) return;
        model.Ready = false;
        model.Note = "Vorschau wird erstellt …";
        var id = Session.Case.Id;
        await Session.Run(async () =>
        {
            var ct = Ct;
            var html = await Task.Run(async () => (await Session.Service.RenderReport(id, false, ct)).Html, ct);
            await ShowHtml(html);
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

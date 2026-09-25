using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

// Id null is the default of the rules, whichever template that is.
public sealed record TemplateChoice(string? Id, string Label);

public sealed class ReportModel : Observable
{
    string note = "", saved = "";
    bool ready, busy;
    List<TemplateChoice> templates = [];
    TemplateChoice? template;

    public List<TemplateChoice> Templates { get => templates; set { if (Set(ref templates, value)) Raise(nameof(ShowTemplates)); } }
    public bool ShowTemplates => templates.Count > 1;
    public TemplateChoice? Template { get => template; set => Set(ref template, value); }

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
    bool picking;

    protected override int Page => (int)Tab.Report;

    public ReportView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Session.CaseChanged += () => { if (IsActive && Session.Case?.TemplateId != model.Template?.Id) Load(); };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ReportModel.Template) && !picking) Pick();
        };
    }

    protected override void OnEnter() => Load();

    protected override void Render(RuleSet rules) => Load();

    // The Prüfung keeps a template of its own only while that template exists; otherwise the default applies.
    void ShowTemplates(Case kase)
    {
        picking = true;
        List<TemplateChoice> choices = [];
        if (Session.Rules is { } rs)
            foreach (var t in RulesView.Templates(rs))
                choices.Add(new TemplateChoice(t.Default ? null : t.Id, t.Default ? t.Name + " (Standard)" : t.Name));
        model.Templates = choices;
        model.Template = choices.Find(c => c.Id == kase.TemplateId) ?? choices.FirstOrDefault();
        picking = false;
    }

    async void Pick()
    {
        if (Session.Case is not { } kase || model.Template is not { } choice || kase.TemplateId == choice.Id) return;
        kase.TemplateId = choice.Id;
        if (!await Session.SaveCase(At("template"), CancellationToken.None)) return;
        Load();
    }

    async void Load()
    {
        if (Session.Case is null) return;
        ShowTemplates(Session.Case);
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
        try
        {
            model.Note = await Web.Show(html, TimeSpan.FromSeconds(20), Ct) ? "" : Unavailable;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            model.Note = Unavailable;
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
            if (resp is not { Pdf: { } pdf, FileName: { } file })
            {
                Session.Fail("PDF konnte nicht erstellt werden");
                return;
            }
            model.Busy = false;
            if (await Session.SaveFile(file, pdf, Session.PdfFilter) is { } name)
                model.Saved = "Gespeichert: " + name;
        });
        model.Busy = false;
    }
}

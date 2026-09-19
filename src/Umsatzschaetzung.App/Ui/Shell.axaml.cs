using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public partial class Shell : Window
{
    readonly Session session;
    readonly CasesView cases;
    readonly Screen[] screens;
    readonly Dictionary<ImportJob, ImportWindow> imports = [];
    Screen? current;
    RulesWindow? rules;

    public Shell(IService service)
    {
        InitializeComponent();
        session = new Session(service) { Owner = this };
        session.Imports.Jobs.CollectionChanged += ImportsChanged;
        cases = new CasesView(session);
        CasesHost.Content = cases;
        screens =
        [
            new CaseView(session),
            new InvoicesView(session),
            new MappingView(session),
            new CalcView(session),
            new ReportView(session),
        ];
        for (var i = 0; i < screens.Length; i++) ((TabItem)Tabs.Items[i]!).Content = screens[i];
        session.CaseOpened += OpenCase;
        session.CaseChanged += RefreshContext;
        session.StatusChanged += RefreshError;
        session.RulesRequested += ShowRules;
        session.TabRequested += tab => Tabs.SelectedIndex = (int)tab;
        session.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Session.Error)) RefreshError(); };
        Loaded += async (_, _) =>
        {
            Show(cases);
            await session.LoadStatus(CancellationToken.None);
            await session.LoadRules(CancellationToken.None);
        };
        Closed += (_, _) =>
        {
            session.Imports.CancelAll();
            current?.Leave();
            rules?.Close();
        };
    }

    void ImportsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (ImportJob job in e.OldItems ?? Array.Empty<ImportJob>())
            if (imports.Remove(job, out var window) && job.Summary == "") window.Close();
        foreach (ImportJob job in e.NewItems ?? Array.Empty<ImportJob>())
        {
            var window = new ImportWindow(job);
            imports[job] = window;
            window.Show(this);
        }
    }

    void ShowRules(object? sender, RoutedEventArgs e) => ShowRules();

    void ShowRules()
    {
        if (rules is null)
        {
            rules = new RulesWindow(session);
            rules.Closed += (_, _) => rules = null;
            rules.Show();
        }
        else rules.Activate();
    }

    void Show(Screen screen)
    {
        if (current == screen) return;
        session.Error = "";
        current?.Leave();
        current = screen;
        screen.Enter();
    }

    void TabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source == Tabs && Tabs.SelectedIndex >= 0 && CaseUi.IsVisible) Show(screens[Tabs.SelectedIndex]);
    }

    void OpenCase(CaseResp resp)
    {
        RefreshContext();
        CasesHost.IsVisible = false;
        CaseUi.IsVisible = true;
        Tabs.SelectedIndex = 0;
        Show(screens[0]);
    }

    void Back(object? sender, RoutedEventArgs e)
    {
        current?.Leave();
        current = null;
        session.CloseCase();
        CaseUi.IsVisible = false;
        CasesHost.IsVisible = true;
        Title = "Umsatzschätzung";
        Show(cases);
    }

    void RefreshContext()
    {
        if (session.Case is null) return;
        CaseLabel.Text = session.Case.Label;
        CasePeriod.Text = session.Display?.Period ?? "";
        Title = "Umsatzschätzung: " + session.Case.Label;
    }

    void DismissError(object? sender, RoutedEventArgs e) => session.Error = "";

    void RefreshError()
    {
        var text = session.Error != "" ? session.Error : session.Status?.Problem ?? "";
        ErrorText.Text = text;
        ErrorBanner.IsVisible = text != "";
        ErrorClose.IsVisible = session.Error != "";
    }
}

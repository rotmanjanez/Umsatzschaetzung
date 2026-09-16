using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shell;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

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
        session = new Session(service);
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
        for (var i = 0; i < screens.Length; i++) ((TabItem)Tabs.Items[i]).Content = screens[i];
        session.CaseOpened += OpenCase;
        session.CaseChanged += RefreshContext;
        session.StatusChanged += RefreshError;
        session.RulesRequested += ShowRules;
        session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Session.Message)) RefreshStatus();
            if (e.PropertyName == nameof(Session.Error)) RefreshError();
        };
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
        {
            job.PropertyChanged -= ImportProgressed;
            if (imports.Remove(job, out var window)) window.Close();
        }
        foreach (ImportJob job in e.NewItems ?? Array.Empty<ImportJob>())
        {
            var window = new ImportWindow(job) { Owner = this };
            imports[job] = window;
            job.PropertyChanged += ImportProgressed;
            window.Show();
        }
        RefreshTaskbar();
    }

    void ImportProgressed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ImportJob.Fraction)) RefreshTaskbar();
    }

    void RefreshTaskbar()
    {
        var jobs = session.Imports.Jobs;
        Taskbar.ProgressState = jobs.Count == 0 ? TaskbarItemProgressState.None : TaskbarItemProgressState.Normal;
        Taskbar.ProgressValue = jobs.Count == 0 ? 0 : jobs.Average(j => j.Fraction);
    }

    void ShowRules(object sender, RoutedEventArgs e) => ShowRules();

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

    void TabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.OriginalSource == Tabs && Tabs.SelectedIndex >= 0 && CaseUi.IsVisible) Show(screens[Tabs.SelectedIndex]);
    }

    void OpenCase(CaseResp resp)
    {
        RefreshContext();
        CasesHost.Visibility = Visibility.Collapsed;
        CaseUi.Visibility = Visibility.Visible;
        Tabs.SelectedIndex = 0;
        Show(screens[0]);
    }

    void Back(object sender, RoutedEventArgs e)
    {
        current?.Leave();
        current = null;
        session.CloseCase();
        CaseUi.Visibility = Visibility.Collapsed;
        CasesHost.Visibility = Visibility.Visible;
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

    void DismissError(object sender, RoutedEventArgs e) => session.Error = "";

    void RefreshError()
    {
        var text = session.Error != "" ? session.Error : session.Status?.Problem ?? "";
        ErrorText.Text = text;
        ErrorBanner.Visibility = text == "" ? Visibility.Collapsed : Visibility.Visible;
        ErrorClose.Visibility = session.Error == "" ? Visibility.Collapsed : Visibility.Visible;
    }

    void RefreshStatus() => StatusText.Text = session.Message;
}

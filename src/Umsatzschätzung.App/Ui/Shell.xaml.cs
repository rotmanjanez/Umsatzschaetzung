using System.Windows;
using System.Windows.Controls;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public partial class Shell : Window
{
    readonly Session session;
    readonly CasesView cases;
    readonly Screen[] screens;
    readonly RadioButton[] buttons;
    Screen? current;
    RulesWindow? rules;

    public Shell(IService service)
    {
        InitializeComponent();
        session = new Session(service);
        ImportList.ItemsSource = session.Imports.Jobs;
        cases = new CasesView(session);
        screens =
        [
            new CaseView(session),
            new InvoicesView(session),
            new MappingView(session),
            new CalcView(session),
            new ReportView(session),
        ];
        buttons = [NavCase, NavInvoices, NavMapping, NavCalc, NavReport];
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

    void CancelImport(object sender, RoutedEventArgs e) => ((ImportJob)((Button)sender).DataContext).Cancel();

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
        Body.Content = screen;
        screen.Enter();
    }

    void NavChecked(object sender, RoutedEventArgs e)
    {
        var i = Array.IndexOf(buttons, (RadioButton)sender);
        if (i >= 0) Show(screens[i]);
    }

    void OpenCase(CaseResp resp)
    {
        RefreshContext();
        Nav.Visibility = Visibility.Visible;
        buttons[0].IsChecked = true;
        Show(screens[0]);
    }

    void Back(object sender, RoutedEventArgs e)
    {
        current?.Leave();
        current = null;
        session.CloseCase();
        Nav.Visibility = Visibility.Collapsed;
        foreach (var b in buttons) b.IsChecked = false;
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

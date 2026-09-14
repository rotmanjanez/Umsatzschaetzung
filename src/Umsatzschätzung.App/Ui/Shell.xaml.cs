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

    public Shell(IService service)
    {
        InitializeComponent();
        session = new Session(service);
        cases = new CasesView(session);
        screens =
        [
            new CaseView(session),
            new InvoicesView(session),
            new MappingView(session),
            new RulesView(session),
            new CalcView(session),
            new ReportView(session),
        ];
        buttons = [NavCase, NavInvoices, NavMapping, NavRules, NavCalc, NavReport];
        session.CaseOpened += OpenCase;
        session.CaseChanged += RefreshContext;
        session.StatusChanged += RefreshError;
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
        Closed += (_, _) => current?.Leave();
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

using System.Windows;

namespace Umsatzschätzung.App.Ui;

public partial class RulesWindow : Window
{
    readonly RulesView view;

    public RulesWindow(Session session)
    {
        InitializeComponent();
        view = new RulesView(session);
        Body.Content = view;
        view.Enter();
        Closed += (_, _) => view.Leave();
    }
}

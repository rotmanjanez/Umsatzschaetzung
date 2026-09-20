using Avalonia.Controls;

namespace Umsatzschaetzung.App.Ui;

public partial class RulesWindow : Window
{
    readonly RulesView view;

    public RulesWindow(Session session)
    {
        InitializeComponent();
        view = new RulesView(session);
        Body.Content = view;
        view.Enter();
        Help.OnF1(this, () => view.Topic);
        Closed += (_, _) => view.Leave();
    }
}

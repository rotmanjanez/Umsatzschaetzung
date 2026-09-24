using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public partial class RulesWindow : Window
{
    readonly Session session;
    readonly RulesView view;

    public RulesWindow(Session session)
    {
        InitializeComponent();
        this.session = session;
        view = new RulesView(session);
        Body.Content = view;
        view.Enter();
        Help.OnF1(this, () => view.Topic);
        session.Anchor(this, ErrorBanner, ErrorText);
        Closed += (_, _) => view.Leave();
    }

    void DismissError(object? sender, RoutedEventArgs e) => session.Error = "";

    public void NewProduct(string name, Action<string> created) => view.NewProduct(name, created);

    public void EditProduct(string id, List<RecipeLine>? recipe) => view.EditProduct(id, recipe);
}

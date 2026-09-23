using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;

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
        session.PropertyChanged += ErrorChanged;
        Activated += (_, _) => session.ActiveWindow = this;
        Closed += (_, _) =>
        {
            session.PropertyChanged -= ErrorChanged;
            if (session.ActiveWindow == this) session.ActiveWindow = null;
            if (session.ErrorWindow == this) session.Error = "";
            view.Leave();
        };
    }

    void ErrorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Session.Error)) return;
        ErrorText.Text = session.Error;
        ErrorBanner.IsVisible = session.ErrorWindow == this;
    }

    void DismissError(object? sender, RoutedEventArgs e) => session.Error = "";

    public void NewProduct(string name, Action<string> created) => view.NewProduct(name, created);
}

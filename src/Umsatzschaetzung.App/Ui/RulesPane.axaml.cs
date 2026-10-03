using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public partial class RulesPane : UserControl
{
    readonly Session session;
    readonly RulesView view;

    RulesPane(Session session)
    {
        InitializeComponent();
        this.session = session;
        view = new RulesView(session);
        Body.Content = view;
        Frame = Frame.For(this, "Regeln", 1100, 760, 820, 520);
        view.Enter();
        Help.OnF1(Frame.Input, () => view.Topic);
        History.Keys(Frame.Input, view.Move, textFirst: () => view.TypingFirst);
        session.Anchor(Frame, ErrorBanner, ErrorText);
        Frame.Closed += session.Indicate(this, SaveBadge, SaveText);
        Frame.Closed += view.Leave;
        Frame.Opening += view.FocusPage;
    }

    public Frame Frame { get; }

    // Not owned by the program's window: the rules stay open beside it.
    public static RulesPane Open(Session session)
    {
        var pane = new RulesPane(session);
        pane.Frame.Show(null);
        return pane;
    }

    void DismissError(object? sender, RoutedEventArgs e) => session.Error = "";

    public void NewProduct(string name, Action<string> created) => view.NewProduct(name, created);

    public void EditProduct(string id, List<PartLine>? recipe) => view.EditProduct(id, recipe);
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Umsatzschaetzung.App.Ui;

public abstract class Screen : UserControl
{
    CancellationTokenSource? cts;

    protected Screen(Session session) => Session = session;

    public Session Session { get; }

    public abstract string Topic { get; }

    protected void ShowHelp(object? sender, RoutedEventArgs e) =>
        Help.Open(TopLevel.GetTopLevel(this) as Window, Topic);

    public bool IsActive => cts is not null;

    protected CancellationToken Ct => cts?.Token ?? new CancellationToken(true);

    public void Enter()
    {
        if (cts is not null) return;
        cts = new CancellationTokenSource();
        OnEnter();
    }

    public void Leave()
    {
        if (cts is null) return;
        OnLeave();
        cts.Cancel();
        cts.Dispose();
        cts = null;
    }

    protected virtual void OnEnter() { }

    protected virtual void OnLeave() { }
}

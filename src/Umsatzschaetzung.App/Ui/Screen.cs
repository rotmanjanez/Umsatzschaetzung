using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Umsatzschaetzung.App.Ui;

public abstract class Screen : UserControl
{
    CancellationTokenSource? cts;
    string? reveal;

    protected Screen(Session session) => Session = session;

    public Session Session { get; }

    public abstract string Topic { get; }

    protected virtual History History => Session.History;

    protected virtual int Page => 0;

    // Where a change on this screen is made, for undoing it to come back here.
    protected Place At(string item = "") => new(History, Page, item);

    protected void ShowHelp(object? sender, RoutedEventArgs e) =>
        Help.Open(TopLevel.GetTopLevel(this) as Window, Topic);

    public bool IsActive => cts is not null;

    protected CancellationToken Ct => cts?.Token ?? new CancellationToken(true);

    public void Enter(string? item = null)
    {
        if (cts is not null) return;
        reveal = item;
        cts = new CancellationTokenSource();
        OnEnter();
    }

    // The item an undo came back to, handed out once for the screen to mark when it has loaded.
    protected string? Revealing()
    {
        var item = reveal;
        reveal = null;
        return item;
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

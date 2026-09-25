using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschaetzung.Model;

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
        Session.RulesChanged += Redraw;
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
        Session.RulesChanged -= Redraw;
        OnLeave();
        cts.Cancel();
        cts.Dispose();
        cts = null;
    }

    protected virtual void OnEnter() { }

    // Everything a screen shows of the rules is drawn here and nowhere else: once they are loaded on
    // entering, and again after every change, in whichever window it was made.
    protected abstract void Render(RuleSet rules);

    // Should the store be out of reach, the screen is drawn from the rules last known.
    protected async Task LoadRules()
    {
        if (!await Session.LoadRules(Ct) && IsActive) Redraw();
    }

    void Redraw()
    {
        if (Session.Rules is { } rules) Render(rules);
    }

    protected virtual void OnLeave() { }
}

using System.Windows.Controls;

namespace Umsatzschätzung.App.Ui;

public class Screen : UserControl
{
    CancellationTokenSource? cts;

    protected Screen(Session session) => Session = session;

    protected Session Session { get; }

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

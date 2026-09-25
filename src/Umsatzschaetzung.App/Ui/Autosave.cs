using Avalonia.Threading;

namespace Umsatzschaetzung.App.Ui;

// Edits wait for a pause in typing; picking another entry, undoing or leaving saves at once.
public sealed class Autosave
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Func<Task> save;
    int running;

    public Autosave(Func<Task> save)
    {
        this.save = save;
        timer.Tick += (_, _) => _ = Now();
    }

    // While true the form holds more than the store, so a rebuild must not load over it.
    public bool Busy => timer.IsEnabled || running > 0;

    public void Schedule()
    {
        timer.Stop();
        timer.Start();
    }

    public void Cancel() => timer.Stop();

    public async Task Now()
    {
        if (!timer.IsEnabled) return;
        timer.Stop();
        running++;
        try
        {
            await save();
        }
        finally
        {
            running--;
        }
    }
}

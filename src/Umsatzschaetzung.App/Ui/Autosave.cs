using Avalonia.Threading;

namespace Umsatzschaetzung.App.Ui;

// Edits wait for a pause in typing; picking another entry, undoing or leaving saves at once.
// The save is told whether the user is done with the form: only then is what is missing pointed out.
public sealed class Autosave
{
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    readonly Func<bool, Task<bool>> save;
    Task<bool> last = Task.FromResult(true);
    int running, started;
    bool due;

    public Autosave(Func<bool, Task<bool>> save)
    {
        this.save = save;
        timer.Tick += (_, _) => _ = Now();
    }

    // While true the form holds more than the store, so a rebuild must not load over it.
    public bool Busy => timer.IsEnabled || running > 0 || due;

    public void Schedule()
    {
        timer.Stop();
        timer.Start();
    }

    // An edit that waits to be saved until the field is left, not for a pause.
    public void Mark() => due = true;

    // The form is filled anew: what it held before is no longer to be written.
    public void Cancel()
    {
        timer.Stop();
        due = false;
        started++;
        last = Task.FromResult(true);
    }

    // False while something typed is not in the store: it was incomplete, or the write failed.
    public Task<bool> Now(bool done = false)
    {
        if (!timer.IsEnabled && !due) return last;
        timer.Stop();
        due = false;
        return last = Run(done);
    }

    async Task<bool> Run(bool done)
    {
        var n = ++started;
        running++;
        try
        {
            var saved = await save(done);
            if (!saved && n == started) due = true;
            return saved;
        }
        finally
        {
            running--;
        }
    }
}

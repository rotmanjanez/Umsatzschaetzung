using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Umsatzschaetzung.App.Ui;

// Where a change was made: the history of its window, the page in that window and the item on it.
// Undoing the change goes back there and marks the item.
public sealed record Place(History History, int Page, string Item = "");

public interface IChange
{
    // Changes to one target within a step fold into one: its first before, its last after.
    object Target { get; }
    IChange Then(IChange later);
    // Why the change can no longer be set back, or brought again; null when it can.
    string? Stale(bool back);
    Task<bool> Apply(bool back);
}

// One window's changes, newest last. Changes to the same item in quick succession are one step,
// so a word typed is undone as a word.
public sealed class History(Session session)
{
    static readonly TimeSpan Together = TimeSpan.FromSeconds(1.5);
    const int Depth = 200;

    readonly List<Step> undo = [], redo = [];
    bool moving;

    public bool CanUndo => undo.Count > 0 && !moving;
    public bool CanRedo => redo.Count > 0 && !moving;

    public static string Overtaken(bool back) =>
        $"Das lässt sich nicht mehr {(back ? "rückgängig machen" : "wiederherstellen")}: es wurde inzwischen an anderer Stelle geändert.";

    public void Record(Place at, IChange change)
    {
        redo.Clear();
        var now = DateTime.UtcNow;
        if (undo.Count > 0 && undo[^1] is var last && last.At == at && now - last.When < Together)
        {
            var i = last.Changes.FindIndex(c => c.Target.Equals(change.Target));
            if (i >= 0) last.Changes[i] = last.Changes[i].Then(change);
            else last.Changes.Add(change);
            last.When = now;
            return;
        }
        undo.Add(new Step(at, [change], now));
        if (undo.Count > Depth) undo.RemoveAt(0);
    }

    public void Clear()
    {
        undo.Clear();
        redo.Clear();
    }

    public Task<Place?> Undo() => Move(undo, redo, true);

    public Task<Place?> Redo() => Move(redo, undo, false);

    // A step another window has overtaken is dropped rather than applied in part.
    async Task<Place?> Move(List<Step> from, List<Step> to, bool back)
    {
        if (moving || from.Count == 0) return null;
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        var changes = back ? Enumerable.Reverse(step.Changes).ToList() : step.Changes;
        var applied = true;
        moving = true;
        try
        {
            if (changes.Select(c => c.Stale(back)).FirstOrDefault(s => s is not null) is { } stale)
            {
                session.Fail(stale);
                return null;
            }
            foreach (var c in changes)
                if (!await c.Apply(back)) applied = false;
        }
        finally
        {
            moving = false;
        }
        if (!applied) return null;
        to.Add(step);
        if (undo.Count > 0) undo[^1].When = default;
        return step.At;
    }

    // The platform's undo and redo keys, taken before any control sees them. Where a page saves only
    // on request, a text field keeps the keys while it has typing of its own to take back.
    public static void Keys(TopLevel window, Func<bool, Task> move, Func<bool>? textFirst = null)
    {
        void Pressed(object? sender, KeyEventArgs e)
        {
            if (Application.Current?.PlatformSettings?.HotkeyConfiguration is not { } keys) return;
            bool? back = keys.Undo.Any(g => g.Matches(e)) ? true : keys.Redo.Any(g => g.Matches(e)) ? false : null;
            if (back is not { } b) return;
            if (textFirst?.Invoke() == true && window.FocusManager?.GetFocusedElement() is TextBox box && (b ? box.CanUndo : box.CanRedo)) return;
            e.Handled = true;
            _ = move(b);
        }
        window.AddHandler(InputElement.KeyDownEvent, Pressed, RoutingStrategies.Tunnel);
    }

    sealed class Step(Place at, List<IChange> changes, DateTime when)
    {
        public Place At { get; } = at;
        public List<IChange> Changes { get; } = changes;
        public DateTime When { get; set; } = when;
    }
}

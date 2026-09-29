using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Headless;

namespace Umsatzschaetzung.Lessons;

// One step of an exercise: where the learner may act, and what the program shows once it is done.
public sealed record Goal
{
    public List<Target> Allow { get; init; } = [];
    public required Done Done { get; init; }
}

// `at` is there; with `selected` its row, tab or option is chosen; with `text` its field reads one of them.
public sealed record Done
{
    public required Target At { get; init; }
    public bool Selected { get; init; }
    public List<string>? Text { get; init; }
}

// Runs the real program under an exercise: a press or a key only reaches the controls the
// current step allows, and the step ends when the program shows what it asks for. What to
// say about it is the page's business; this only reports misses and success.
public static partial class Coach
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    static TopLevel? top;
    static Goal? task;
    static bool swallowed, acted;

    public static void Attach(TopLevel host, Session work)
    {
        top = host;
        session = work;
        // Class handlers run before the program's own, which also listen on the top level.
        Gate(InputElement.PointerPressedEvent, Press);
        Gate(InputElement.PointerReleasedEvent, Release);
        Gate(InputElement.KeyDownEvent, Key);
        Gate(InputElement.KeyUpEvent, Key);
        Gate(InputElement.TextInputEvent, Key);
        new DispatcherTimer(TimeSpan.FromMilliseconds(150), DispatcherPriority.Background, (_, _) => Check()).Start();
    }

    static void Gate<T>(RoutedEvent<T> e, Action<T> handler) where T : RoutedEventArgs =>
        e.AddClassHandler<TopLevel>((_, args) => handler(args), RoutingStrategies.Tunnel, handledEventsToo: true);

    // The page hands over the next step, or nothing while it talks.
    [JSExport]
    public static void Step(string? json)
    {
        task = string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<Goal>(json, Options);
        acted = false;
    }

    // Where a control sits, in the page's pixels over the program, for a spotlight or a ring;
    // empty while it is not there yet or a list is still scrolling it into view. With `show`, a
    // control its panel has scrolled away is scrolled back into view.
    [JSExport]
    public static string Box(string json, bool show)
    {
        var target = JsonSerializer.Deserialize<Target>(json, Options);
        if (top is null || target is null) return "";
        if (Find(target) is not { } hit)
        {
            Targets.Reveal(top, target);
            return "";
        }
        if (show) Show(hit);
        var box = Targets.Box(top, hit);
        return JsonSerializer.Serialize(new[] { box.X, box.Y, box.Width, box.Height });
    }

    // Every control that reads as asked, as far as its list shows it: rows a grid keeps for
    // reuse stand in its tree in no order, so the first one found is any of them.
    [JSExport]
    public static string Span(string json)
    {
        var target = JsonSerializer.Deserialize<Target>(json, Options);
        if (top is null || target is null) return "";
        Rect? span = null;
        foreach (var hit in top.GetVisualDescendants().Where(v => Targets.Is(v, target)))
            if ((target.Up is { } up ? Targets.Above(hit, up) : hit) is { } visual && Shown(visual) is { } box)
                span = span?.Union(box) ?? box;
        return span is { } r ? JsonSerializer.Serialize(new[] { r.X, r.Y, r.Width, r.Height }) : "";
    }

    static Rect? Shown(Visual visual)
    {
        var box = Targets.Box(top!, visual);
        if (visual.GetVisualAncestors().FirstOrDefault(a => a is ScrollViewer or DataGridRowsPresenter) is { } view)
            box = box.Intersect(Targets.Box(top!, view));
        return box.Width > 0 && box.Height > 0 ? box : null;
    }

    // The page asks several times a second, so what was found is kept as long as it is still
    // shown and still reads as asked; only then is the whole tree searched again.
    static readonly Dictionary<Target, (Visual Hit, Visual Found)> found = [];

    static Visual? Find(Target target)
    {
        if (top is null) return null;
        if (found.TryGetValue(target, out var known) && known.Hit.IsAttachedToVisualTree() && Targets.Is(known.Hit, target))
            return known.Found;
        if (Targets.Seek(top, target, out var hit) is not { } result)
        {
            found.Remove(target);
            return null;
        }
        found[target] = (hit!, result);
        return result;
    }

    static void Show(Visual visual)
    {
        if (visual is not Control control || visual.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() is not { } viewer) return;
        if (visual.TranslatePoint(default, viewer) is not { } at) return;
        if (!new Rect(viewer.Bounds.Size).Contains(new Rect(at, visual.Bounds.Size))) control.BringIntoView();
    }

    [JSImport("missed", "coach")]
    static partial void Missed(double x, double y);

    [JSImport("solved", "coach")]
    static partial void Solved();

    static void Press(PointerPressedEventArgs e)
    {
        var left = e.GetCurrentPoint(top).Properties.IsLeftButtonPressed;
        swallowed = !left || !Allowed(e.Source as Visual);
        acted |= !swallowed;
        if (!swallowed) return;
        e.Handled = true;
        var at = e.GetPosition(top);
        if (left) Missed(at.X, at.Y);
    }

    static void Release(PointerReleasedEventArgs e)
    {
        if (swallowed) e.Handled = true;
    }

    static void Key(RoutedEventArgs e)
    {
        if (Allowed(Focused())) acted = true;
        else if (!Copies(e)) e.Handled = true;
    }

    // A grid keeps the focus itself while a cell is chosen, and typing there edits that cell.
    static Visual? Focused() => top?.FocusManager?.GetFocusedElement() switch
    {
        DataGrid { CurrentColumn: { } column, SelectedItem: { } item } => column.GetCellContent(item)?.FindAncestorOfType<DataGridCell>(),
        var focused => focused as Visual,
    };

    // Copying changes nothing, so it is never kept from the learner.
    static bool Copies(RoutedEventArgs e) =>
        e is KeyEventArgs key && top?.GetPlatformSettings()?.HotkeyConfiguration.Copy.Any(g => g.Matches(key)) == true;

    // A popup, such as the calendar of a date field, belongs to the control that opened it.
    static bool Allowed(Visual? visual) =>
        visual is not null && top is not null && task is not null
        && (task.Allow.Any(t => Find(t) is { } hit && (hit == visual || hit.IsVisualAncestorOf(visual)))
            || visual.GetLogicalAncestors().OfType<Popup>().FirstOrDefault()?.PlacementTarget is { } owner && Allowed(owner));

    static void Check()
    {
        if (top is null || task is null) return;
        // The program often gets there on its own, such as choosing the next open line: only
        // the learner's own press or key solves a step.
        if (!acted || Find(task.Done.At) is not { } hit || !Met(hit, task.Done)) return;
        task = null;
        Solved();
    }

    static bool Met(Visual hit, Done done)
    {
        if (done.Selected && !Chosen(hit)) return false;
        return done.Text is null || Field(hit) is { } text && done.Text.Any(t => Same(t, text));
    }

    // The nearest row, tab or option around what was found decides, not the tab around all of them.
    static bool Chosen(Visual hit) =>
        hit.GetSelfAndVisualAncestors().OfType<StyledElement>().FirstOrDefault(v => v is ToggleButton or ISelectable or DataGridRow)
            is { } chosen && (chosen.Classes.Contains(":selected") || chosen.Classes.Contains(":checked"));

    static string? Field(Visual hit) =>
        (hit as TextBox ?? hit.GetVisualDescendants().OfType<TextBox>().FirstOrDefault())?.Text;

    // An amount reads right at the value the program keeps from it, as 4,6 does for 4,60.
    static bool Same(string a, string b) =>
        Input.Micro(a) is { } x && x == Input.Micro(b) ||
        string.Equals(string.Join(' ', a.Split(' ', StringSplitOptions.RemoveEmptyEntries)),
            string.Join(' ', b.Split(' ', StringSplitOptions.RemoveEmptyEntries)), StringComparison.OrdinalIgnoreCase);
}

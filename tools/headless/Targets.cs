using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.Headless;

// What a step reaches for: a name from the XAML, a visible text, a control
// type - and optionally the ancestor it actually means.
public sealed record Target
{
    public string? Name { get; init; }
    public string? Text { get; init; }
    public string? Starts { get; init; }
    public string? Type { get; init; }
    public string? Tip { get; init; }
    public string? Up { get; init; }
}

// Shared by the headless driver and the exercise in the browser, so a script and an
// exercise mean the same control by the same words.
public static class Targets
{
    // A row a list has scrolled out of sight is scrolled into view and looked for again.
    public static Visual Find(Visual root, Target target, Action settle)
    {
        if (Seek(root, target) is { } hit) return hit;
        if (Reveal(root, target))
        {
            settle();
            if (Seek(root, target) is { } revealed) return revealed;
        }
        throw new InvalidOperationException("not found: " + target);
    }

    public static Visual? Seek(Visual root, Target target) => Seek(root, target, out _);

    // Also hands back the control that matched, before `up` led to its ancestor.
    public static Visual? Seek(Visual root, Target target, out Visual? hit)
    {
        hit = root.GetVisualDescendants().FirstOrDefault(v => Is(v, target));
        return hit is null ? null : target.Up is { } up ? Up(hit, up) : hit;
    }

    public static bool Is(Visual v, Target target) =>
        v.IsEffectivelyVisible
        && (target.Name is null || (v as StyledElement)?.Name == target.Name)
        && (target.Text is null || Label(v) == target.Text)
        && (target.Starts is null || Label(v)?.StartsWith(target.Starts, StringComparison.Ordinal) == true)
        && (target.Type is null || v.GetType().Name == target.Type)
        && (target.Tip is null || v is Control c && ToolTip.GetTip(c) as string == target.Tip);

    public static bool Reveal(Visual root, Target target)
    {
        if (target.Text is null && target.Starts is null) return false;
        bool Matches(string? s) => s is not null && (target.Text is { } t ? s == t : s.StartsWith(target.Starts!, StringComparison.Ordinal));
        foreach (var grid in root.GetVisualDescendants().OfType<DataGrid>().Where(g => g.IsEffectivelyVisible))
        {
            var row = grid.ItemsSource?.Cast<object>().FirstOrDefault(item => item.GetType().GetProperties()
                .Any(p => p.PropertyType == typeof(string) && p.GetIndexParameters().Length == 0 && Matches(p.GetValue(item) as string)));
            if (row is null) continue;
            grid.ScrollIntoView(row, null);
            return true;
        }
        return false;
    }

    public static Visual Up(Visual inner, string type) =>
        inner.GetVisualAncestors().FirstOrDefault(v => v.GetType().Name == type)
        ?? throw new InvalidOperationException("no " + type + " above " + inner.GetType().Name);

    public static string? Label(Visual visual) => visual switch
    {
        TextBlock t => t.Text,
        HeaderedContentControl h => h.Header as string,
        ContentControl c => c.Content as string,
        _ => null,
    };

    public static Rect Box(Visual root, Visual visual) =>
        new(visual.TranslatePoint(default, root) ?? default, visual.Bounds.Size);
}

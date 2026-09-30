using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

// What an undo or redo set back blinks where it is shown.
public static class Reveal
{
    // Run on the one control it is for: as a style's animation every control built would set it up.
    static readonly Animation Blink = new()
    {
        Duration = TimeSpan.FromSeconds(0.3),
        IterationCount = new IterationCount(4),
        PlaybackDirection = PlaybackDirection.Alternate,
        Children =
        {
            new KeyFrame { Cue = new Cue(0), Setters = { new Setter(Visual.OpacityProperty, 1.0) } },
            new KeyFrame { Cue = new Cue(1), Setters = { new Setter(Visual.OpacityProperty, 0.3) } },
        },
    };

    public static void Flash(Control? control)
    {
        if (control is null) return;
        control.BringIntoView();
        _ = Blink.RunAsync(control);
    }

    // The outermost visual showing the item, once layout has placed it.
    public static void Flash(Control scope, object item) =>
        Dispatcher.UIThread.Post(() => Flash(scope.GetVisualDescendants().OfType<Control>()
            .FirstOrDefault(c => ReferenceEquals(c.DataContext, item))), DispatcherPriority.Background);

    public static void Row(Control list, object? item)
    {
        if (item is null) return;
        switch (list)
        {
            case DataGrid grid:
                grid.SelectedItem = item;
                grid.ScrollIntoView(item, null);
                break;
            case ListBox box:
                box.SelectedItem = item;
                box.ScrollIntoView(item);
                break;
        }
        Flash(list, item);
    }
}

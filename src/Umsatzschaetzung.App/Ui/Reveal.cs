using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

// What an undo or redo set back blinks where it is shown.
public static class Reveal
{
    static readonly TimeSpan Blink = TimeSpan.FromSeconds(1.3);

    public static void Flash(Control? control)
    {
        if (control is null) return;
        control.BringIntoView();
        control.Classes.Add("revealed");
        DispatcherTimer.RunOnce(() => control.Classes.Remove("revealed"), Blink);
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

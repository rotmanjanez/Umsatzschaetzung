using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

public static class Blur
{
    // A grid driving a detail pane keeps its selection while that pane is being worked in.
    public static readonly AttachedProperty<Control?> DetailProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, Control?>("Detail", typeof(Blur));

    public static void SetDetail(DataGrid grid, Control? value) => grid.SetValue(DetailProperty, value);
    public static Control? GetDetail(DataGrid grid) => grid.GetValue(DetailProperty);

    // A click on a bare surface moves no focus, so a list kept its focused row until something focusable was hit.
    public static void Register() =>
        InputElement.PointerPressedEvent.AddClassHandler<Window>(Pressed, RoutingStrategies.Bubble, handledEventsToo: true);

    static void Pressed(Window window, PointerPressedEventArgs e)
    {
        if (window.FocusManager?.GetFocusedElement() is not Visual focused) return;
        var list = focused.FindAncestorOfType<DataGrid>(true) ?? (Control?)focused.FindAncestorOfType<ListBox>(true);
        if (list is null || Within(list, e.Source)) return;
        if (list is DataGrid grid && !Within(GetDetail(grid), e.Source)) grid.SelectedItem = null;
        window.FocusManager.Focus(null);
    }

    static bool Within(Visual? scope, object? source) =>
        scope is not null && source is Visual visual && (scope == visual || scope.IsVisualAncestorOf(visual));
}

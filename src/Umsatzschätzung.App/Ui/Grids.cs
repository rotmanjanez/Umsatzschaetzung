using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Umsatzschätzung.App.Ui;

public static class Grids
{
    public static readonly AttachedProperty<int> FlexProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, int>("Flex", typeof(Grids), -1);

    static readonly AttachedProperty<double> AppliedProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, double>("Applied", typeof(Grids), double.NaN);

    public static void SetFlex(DataGrid grid, int value) => grid.SetValue(FlexProperty, value);
    public static int GetFlex(DataGrid grid) => grid.GetValue(FlexProperty);

    static Grids() => FlexProperty.Changed.AddClassHandler<DataGrid>((grid, _) =>
    {
        grid.LayoutUpdated -= Stretch;
        grid.LayoutUpdated += Stretch;
    });

    static void Stretch(object? sender, EventArgs e)
    {
        if (sender is not DataGrid grid) return;

        var index = GetFlex(grid);
        if (index < 0 || index >= grid.Columns.Count) return;

        var flex = grid.Columns[index];
        var applied = grid.GetValue(AppliedProperty);
        if (!double.IsNaN(applied) && Math.Abs(flex.ActualWidth - applied) > 1)
        {
            grid.LayoutUpdated -= Stretch;
            return;
        }

        var used = 0.0;
        foreach (var column in grid.Columns)
            if (column != flex && column.IsVisible)
                used += column.ActualWidth;

        var room = grid.Bounds.Width - used - ScrollbarWidth(grid);
        if (room < Math.Max(flex.MinWidth, grid.MinColumnWidth) || Math.Abs(room - flex.ActualWidth) < 1)
        {
            grid.SetValue(AppliedProperty, flex.ActualWidth);
            return;
        }

        flex.Width = new DataGridLength(room);
        grid.SetValue(AppliedProperty, flex.ActualWidth);
    }

    static double ScrollbarWidth(DataGrid grid)
    {
        foreach (var bar in grid.GetVisualDescendants().OfType<ScrollBar>())
            if (bar.Name == "PART_VerticalScrollbar" && bar.IsVisible)
                return bar.Bounds.Width;
        return 0;
    }
}

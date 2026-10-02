using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Umsatzschaetzung.App.Ui;

public static class Grids
{
    // Snapped to device pixels the columns can end up a fraction wider than their share, and that
    // fraction is enough for a horizontal scrollbar: the flex column stays just short of the room.
    const double Slack = 2;

    public static readonly AttachedProperty<int> FlexProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, int>("Flex", typeof(Grids), -1);

    static readonly AttachedProperty<double> AppliedProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, double>("Applied", typeof(Grids), double.NaN);

    static readonly AttachedProperty<ScrollBar?> ScrollbarProperty =
        AvaloniaProperty.RegisterAttached<DataGrid, ScrollBar?>("Scrollbar", typeof(Grids));

    public static void SetFlex(DataGrid grid, int value) => grid.SetValue(FlexProperty, value);
    public static int GetFlex(DataGrid grid) => grid.GetValue(FlexProperty);

    // LayoutUpdated fires after every layout pass, each scrolled frame included, so Stretch must stay cheap.
    static Grids() => FlexProperty.Changed.AddClassHandler<DataGrid>((grid, _) =>
    {
        grid.TemplateApplied -= Capture;
        grid.TemplateApplied += Capture;
        grid.LayoutUpdated -= Stretch;
        grid.LayoutUpdated += Stretch;
    });

    // Every column whole, the flex one at its floor, beside the scrollbar.
    public static double Least(DataGrid grid)
    {
        var flex = grid.Columns[GetFlex(grid)];
        return Others(grid, flex) + Floor(grid, flex) + (grid.GetValue(ScrollbarProperty)?.Width ?? 0) + Slack;
    }

    static double Floor(DataGrid grid, DataGridColumn flex) => Math.Max(flex.MinWidth, grid.MinColumnWidth);

    static double Others(DataGrid grid, DataGridColumn flex)
    {
        var used = 0.0;
        foreach (var column in grid.Columns)
            if (column != flex && column.IsVisible)
                used += column.ActualWidth;
        return used;
    }

    static void Capture(object? sender, TemplateAppliedEventArgs e) =>
        ((DataGrid)sender!).SetValue(ScrollbarProperty, e.NameScope.Find<ScrollBar>("PART_VerticalScrollbar"));

    static void Stretch(object? sender, EventArgs e)
    {
        if (sender is not DataGrid { IsEffectivelyVisible: true } grid) return;

        var index = GetFlex(grid);
        if (index < 0 || index >= grid.Columns.Count) return;

        var flex = grid.Columns[index];
        var applied = grid.GetValue(AppliedProperty);
        if (!double.IsNaN(applied) && Math.Abs(flex.ActualWidth - applied) > 1)
        {
            grid.LayoutUpdated -= Stretch;
            return;
        }

        var bar = grid.GetValue(ScrollbarProperty);
        var room = Math.Max(grid.Bounds.Width - Others(grid, flex) - (bar is { IsVisible: true } ? bar.Bounds.Width : 0) - Slack, Floor(grid, flex));
        if (Math.Abs(room - flex.ActualWidth) >= 1) flex.Width = new DataGridLength(room);
        grid.SetValue(AppliedProperty, flex.ActualWidth);
    }
}

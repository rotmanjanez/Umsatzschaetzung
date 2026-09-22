using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;
using Path = Avalonia.Controls.Shapes.Path;

namespace Umsatzschaetzung.App.Ui;

public static class Headers
{
    // The sort glyph holds 32 pixels in every header, unsorted and invisible as well: the
    // caption is trimmed against that gap, and what stays free reads as a box in the
    // background colour laid over the header.
    public static void Register() =>
        Control.LoadedEvent.AddClassHandler<DataGridColumnHeader>((header, _) => Free(header));

    static void Free(DataGridColumnHeader header)
    {
        if (header.GetVisualDescendants().OfType<Path>().FirstOrDefault(p => p.Name == "SortIcon") is not { } icon)
            return;
        if (icon.GetVisualParent() is Grid grid)
            grid.ColumnDefinitions[Grid.GetColumn(icon)].MinWidth = 0;
    }
}

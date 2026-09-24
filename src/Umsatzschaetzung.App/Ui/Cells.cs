using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

public static class Cells
{
    // The grid itself copies whole rows, and a template column has nothing to give it: the current
    // cell is what a click picks, so that is what goes to and comes from the clipboard.
    public static void Register() =>
        InputElement.KeyDownEvent.AddClassHandler<DataGrid>(Pressed, RoutingStrategies.Tunnel);

    static void Pressed(DataGrid grid, KeyEventArgs e)
    {
        if (e.Source is TextBox || TopLevel.GetTopLevel(grid) is not { } top) return;
        var keys = grid.GetPlatformSettings()?.HotkeyConfiguration;
        if (keys is null || grid.SelectedItem is not { } item || grid.CurrentColumn is not { } column) return;

        if (keys.Copy.Any(g => g.Matches(e)) && Text(column, item) is { } text)
        {
            e.Handled = true;
            _ = top.Clipboard?.SetTextAsync(text);
        }
        else if (keys.Paste.Any(g => g.Matches(e)) && !column.IsReadOnly && top.Clipboard is { } clipboard)
        {
            e.Handled = true;
            Paste(grid, column, item, clipboard);
        }
    }

    // What the editor holds, not what the cell shows: a unit prints its name but is edited by its code.
    static string? Text(DataGridColumn column, object item)
    {
        if (column is DataGridTemplateColumn { IsReadOnly: false, CellEditingTemplate: { } template }
            && template.Build(item) is { } editor)
        {
            editor.DataContext = item;
            return Box(editor)?.Text;
        }
        return column.GetCellContent(item)?.GetSelfAndVisualDescendants().OfType<TextBlock>().FirstOrDefault()?.Text;
    }

    static async void Paste(DataGrid grid, DataGridColumn column, object item, IClipboard clipboard)
    {
        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrEmpty(text) || grid.SelectedItem != item || grid.CurrentColumn != column) return;
        if (!grid.BeginEdit()) return;
        if (Box(column.GetCellContent(item)) is not { } box)
        {
            grid.CancelEdit();
            return;
        }
        box.Text = text.Split('\n')[0].Split('\t')[0].TrimEnd('\r');
        grid.CommitEdit();
    }

    static TextBox? Box(Control? editor) => editor?.GetSelfAndVisualDescendants().OfType<TextBox>().FirstOrDefault();
}

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
    public static void Register()
    {
        InputElement.KeyDownEvent.AddClassHandler<DataGrid>(Pressed, RoutingStrategies.Tunnel);
        InputElement.TextInputEvent.AddClassHandler<DataGrid>(Typed);
    }

    // The grid only edits on F2 or a second click; typing into a selected cell replaces it like a sheet.
    static void Typed(DataGrid grid, TextInputEventArgs e)
    {
        if (e.Source is TextBox || string.IsNullOrEmpty(e.Text) || grid.IsReadOnly) return;
        if (grid.SelectedItem is not { } item || grid.CurrentColumn is not { IsReadOnly: false } column) return;
        if (!grid.BeginEdit(e) || Box(column.GetCellContent(item)) is not { } box) return;
        e.Handled = true;
        box.Text = e.Text;
        box.Focus();
        box.ClearSelection();
        box.CaretIndex = box.Text.Length;
    }

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

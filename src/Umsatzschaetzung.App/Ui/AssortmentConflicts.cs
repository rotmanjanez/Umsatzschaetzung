using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public sealed record AssortmentConflict(string Name, CaseProduct Listed, CaseProduct Imported);

public sealed class AssortmentConflicts : UserControl
{
    readonly TaskCompletionSource<HashSet<string>?> answered = new();
    readonly List<(string Id, RadioButton Listed, RadioButton Imported)> choices = [];
    readonly Frame frame;

    HashSet<string>? answer;

    AssortmentConflicts(List<AssortmentConflict> conflicts)
    {
        var grid = new Grid { ColumnDefinitions = new("*,Auto,Auto"), ColumnSpacing = 16, RowSpacing = 6 };
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.Children.Add(Cell(Pick("Bisher", "Alle bisherigen Werte behalten", "ConflictAllListed", false), 0, 1));
        grid.Children.Add(Cell(Pick("Import", "Alle Werte aus der Datei übernehmen", "ConflictAllImported", true), 0, 2));
        foreach (var c in conflicts)
        {
            var row = grid.RowDefinitions.Count;
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var listed = Named(new RadioButton { GroupName = c.Listed.ProductId, Content = Values(c.Listed) },
                $"{c.Name}: bisher {Values(c.Listed)}", "ConflictListed");
            var imported = Named(new RadioButton { GroupName = c.Listed.ProductId, Content = Values(c.Imported), IsChecked = true },
                $"{c.Name}: aus der Datei {Values(c.Imported)}", "ConflictImported");
            choices.Add((c.Listed.ProductId, listed, imported));
            grid.Children.Add(Cell(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }, row, 0));
            grid.Children.Add(Cell(listed, row, 1));
            grid.Children.Add(Cell(imported, row, 2));
        }

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 20,
            MaxWidth = 640,
            Children =
            {
                new TextBlock
                {
                    Text = conflicts.Count == 1
                        ? "Ein Produkt steht schon mit anderem Preis oder anderer USt im Sortiment. Welche Werte sollen gelten?"
                        : $"{conflicts.Count} Produkte stehen schon mit anderem Preis oder anderer USt im Sortiment. Welche Werte sollen gelten?",
                    TextWrapping = TextWrapping.Wrap,
                },
                new ScrollViewer { MaxHeight = 420, Content = grid },
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8,
                    Children =
                    {
                        Action("Abbrechen", "SecondaryButton", "ConflictCancel", false, () => answer = null),
                        Action("Importieren", "PrimaryButton", "ConflictImport", true, () => answer = [.. choices.Where(c => c.Imported.IsChecked == true).Select(c => c.Id)]),
                    },
                },
            },
        };
        frame = Frame.For(this, "Sortiment importieren");
        frame.Closed += () => answered.TrySetResult(answer);
        frame.Opening += () => choices[0].Imported.Focus();
    }

    static T Named<T>(T control, string name, string id) where T : Control
    {
        AutomationProperties.SetName(control, name);
        AutomationProperties.SetAutomationId(control, id);
        return control;
    }

    static string Values(CaseProduct p) =>
        (p.GrossPrice > 0 ? Format.Cents(p.GrossPrice) : "ohne Preis") + " · " + Format.Bp(p.Vat);

    static Control Cell(Control c, int row, int column)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, column);
        return c;
    }

    static void Themed(Button button, string theme)
    {
        if (Application.Current?.TryFindResource(theme, out var found) == true && found is ControlTheme control)
            button.Theme = control;
    }

    Button Pick(string caption, string tip, string id, bool imported)
    {
        var button = Named(new Button { Content = caption }, caption, id);
        ToolTip.SetTip(button, tip);
        AutomationProperties.SetHelpText(button, tip);
        Themed(button, "LinkButton");
        button.Click += (_, _) =>
        {
            foreach (var c in choices) (imported ? c.Imported : c.Listed).IsChecked = true;
        };
        return button;
    }

    Button Action(string caption, string theme, string id, bool preferred, Action decide)
    {
        var button = Named(new Button { Content = caption, MinWidth = 96, IsDefault = preferred, IsCancel = !preferred }, caption, id);
        Themed(button, theme);
        button.Click += (_, _) => { decide(); frame.Close(); };
        return button;
    }

    public static Task<HashSet<string>?> Ask(Control? owner, List<AssortmentConflict> conflicts)
    {
        var dialog = new AssortmentConflicts(conflicts);
        dialog.frame.Show(owner, modal: true);
        return dialog.answered.Task;
    }
}

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.App.Ui;

// Picker for the long ingredient list: tippen filtert nach Name oder Kategorie.
public sealed class IngredientBox : AutoCompleteBox
{
    public IngredientBox()
    {
        FilterMode = AutoCompleteFilterMode.Custom;
        ItemFilter = (text, item) => item is Ingredient i && Matches(text, i);
        MinimumPrefixLength = 0;
        IsTextCompletionEnabled = false;
        PlaceholderText = "Zutat suchen …";
        ValueMemberBinding = new Binding(nameof(Ingredient.Name));
        ItemTemplate = new FuncDataTemplate<Ingredient>((_, _) => Row(), true);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        PopulateComplete();
        SetCurrentValue(IsDropDownOpenProperty, true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (SelectedItem is not null || Text is null or "" || ItemsSource is null) return;
        var hits = ItemsSource.OfType<Ingredient>().Where(i => Matches(Text, i)).Take(2).ToList();
        if (hits.Count == 1) SetCurrentValue(SelectedItemProperty, hits[0]);
        else SetCurrentValue(TextProperty, "");
    }

    static Control Row()
    {
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name[!TextBlock.TextProperty] = new Binding(nameof(Ingredient.Name));
        var category = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        category[!TextBlock.TextProperty] = new Binding(nameof(Ingredient.Category));
        category[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        DockPanel.SetDock(category, Dock.Right);
        return new DockPanel { Children = { category, name } };
    }

    static bool Matches(string? text, Ingredient i)
    {
        var terms = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = i.Name + " " + i.Category;
        return terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

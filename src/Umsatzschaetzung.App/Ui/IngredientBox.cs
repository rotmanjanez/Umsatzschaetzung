using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

// Picker for the long ingredient list: tippen filtert nach Name oder Kategorie.
public sealed class IngredientBox : AutoCompleteBox
{
    // Kategorienamen liegen beim Verweisziel, nicht bei der Zutat. Die Ansicht setzt sie
    // einmal je Regelstand; über den Logikbaum erreichen sie auch Boxen in Vorlagen.
    public static readonly AttachedProperty<IReadOnlyDictionary<string, string>?> CategoryNamesProperty =
        AvaloniaProperty.RegisterAttached<IngredientBox, Control, IReadOnlyDictionary<string, string>?>(
            "CategoryNames", inherits: true);

    public static void SetCategoryNames(Control target, IReadOnlyDictionary<string, string>? value) =>
        target.SetValue(CategoryNamesProperty, value);

    public static IReadOnlyDictionary<string, string>? GetCategoryNames(Control target) =>
        target.GetValue(CategoryNamesProperty);

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    public IngredientBox()
    {
        FilterMode = AutoCompleteFilterMode.Custom;
        ItemFilter = (text, item) => item is Ingredient i && Matches(text, i);
        MinimumPrefixLength = 0;
        IsTextCompletionEnabled = false;
        PlaceholderText = "Zutat suchen …";
        ValueMemberBinding = new Binding(nameof(Ingredient.Name));
        ItemTemplate = new FuncDataTemplate<Ingredient>((i, _) => Row(Category(i)), false);
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

    string Category(Ingredient? i) =>
        i is null ? "" : GetCategoryNames(this)?.GetValueOrDefault(i.CategoryId) ?? "";

    static Control Row(string category)
    {
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name[!TextBlock.TextProperty] = new Binding(nameof(Ingredient.Name));
        var label = new TextBlock
        {
            Text = category,
            FontSize = 12,
            Margin = new Thickness(16, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        label[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        DockPanel.SetDock(label, Dock.Right);
        return new DockPanel { Children = { label, name } };
    }

    bool Matches(string? text, Ingredient i)
    {
        var terms = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = i.Name + " " + Category(i);
        return terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

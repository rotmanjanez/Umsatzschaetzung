using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

// Picker for the long ingredient list: tippen filtert nach Name oder Kategorie, danach
// reiht das Zuordnungsmodell die Zutaten an, die dem Getippten ähneln.
public sealed class IngredientBox : AutoCompleteBox
{
    public static readonly StyledProperty<IReadOnlyList<Ingredient>?> ChoicesProperty =
        AvaloniaProperty.Register<IngredientBox, IReadOnlyList<Ingredient>?>(nameof(Choices));

    public IReadOnlyList<Ingredient>? Choices
    {
        get => GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    // Zutaten-Ids, nach Ähnlichkeit zum Text geordnet.
    public static readonly AttachedProperty<Func<string, CancellationToken, Task<IReadOnlyList<string>>>?> SimilarProperty =
        AvaloniaProperty.RegisterAttached<IngredientBox, Control, Func<string, CancellationToken, Task<IReadOnlyList<string>>>?>(
            "Similar", inherits: true);

    public static void SetSimilar(Control target, Func<string, CancellationToken, Task<IReadOnlyList<string>>>? value) =>
        target.SetValue(SimilarProperty, value);

    public static Func<string, CancellationToken, Task<IReadOnlyList<string>>>? GetSimilar(Control target) =>
        target.GetValue(SimilarProperty);

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
        FilterMode = AutoCompleteFilterMode.None;
        AsyncPopulator = Populate;
        MinimumPrefixLength = 0;
        IsTextCompletionEnabled = false;
        PlaceholderText = "Zutat suchen …";
        ValueMemberBinding = new Binding(nameof(Ingredient.Name));
        ItemTemplate = new FuncDataTemplate<Ingredient>((i, _) => Row(Category(i)), false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChoicesProperty) SetCurrentValue(ItemsSourceProperty, Choices);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        SetCurrentValue(ItemsSourceProperty, Filter(Text));
        PopulateComplete();
        SetCurrentValue(IsDropDownOpenProperty, true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (SelectedItem is not null || Text is null or "" || Choices is null) return;
        var hits = Filter(Text).Take(2).ToList();
        if (hits.Count == 1) SetCurrentValue(SelectedItemProperty, hits[0]);
        else SetCurrentValue(TextProperty, "");
    }

    List<Ingredient> Filter(string? text) => Choices?.Where(i => Matches(text, i)).ToList() ?? [];

    // Die Texttreffer zuerst; was das Modell für ähnlich hält, folgt in seiner Reihenfolge.
    async Task<IEnumerable<object>> Populate(string? text, CancellationToken ct)
    {
        var hits = Filter(text);
        if (string.IsNullOrWhiteSpace(text) || GetSimilar(this) is not { } similar || Choices is null) return hits;
        IReadOnlyList<string> ranked;
        try { ranked = await similar(text, ct); }
        catch (Exception e) when (e is ServiceError or OperationCanceledException) { return hits; }
        var byId = Choices.ToDictionary(i => i.Id, StringComparer.Ordinal);
        var shown = hits.ToHashSet();
        foreach (var id in ranked)
            if (byId.TryGetValue(id, out var i) && shown.Add(i)) hits.Add(i);
        return hits;
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

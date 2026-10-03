using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.VisualTree;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

// Picker for the long product list: tippen filtert nach Name oder Kategorie, danach
// reiht das Zuordnungsmodell die Produkte an, die dem Getippten ähneln.
public sealed class ProductBox : AutoCompleteBox
{
    public static readonly StyledProperty<IReadOnlyList<Product>?> ChoicesProperty =
        AvaloniaProperty.Register<ProductBox, IReadOnlyList<Product>?>(nameof(Choices));

    public IReadOnlyList<Product>? Choices
    {
        get => GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    // Produkt-Ids, nach Ähnlichkeit zum Text geordnet.
    public static readonly AttachedProperty<Func<string, CancellationToken, Task<IReadOnlyList<string>>>?> SimilarProperty =
        AvaloniaProperty.RegisterAttached<ProductBox, Control, Func<string, CancellationToken, Task<IReadOnlyList<string>>>?>(
            "Similar", inherits: true);

    public static void SetSimilar(Control target, Func<string, CancellationToken, Task<IReadOnlyList<string>>>? value) =>
        target.SetValue(SimilarProperty, value);

    public static Func<string, CancellationToken, Task<IReadOnlyList<string>>>? GetSimilar(Control target) =>
        target.GetValue(SimilarProperty);

    // Kategorienamen liegen beim Verweisziel, nicht beim Produkt. Die Ansicht setzt sie
    // einmal je Regelstand; über den Logikbaum erreichen sie auch Boxen in Vorlagen.
    public static readonly AttachedProperty<IReadOnlyDictionary<string, string>?> CategoryNamesProperty =
        AvaloniaProperty.RegisterAttached<ProductBox, Control, IReadOnlyDictionary<string, string>?>(
            "CategoryNames", inherits: true);

    public static void SetCategoryNames(Control target, IReadOnlyDictionary<string, string>? value) =>
        target.SetValue(CategoryNamesProperty, value);

    public static IReadOnlyDictionary<string, string>? GetCategoryNames(Control target) =>
        target.GetValue(CategoryNamesProperty);

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    public ProductBox()
    {
        FilterMode = AutoCompleteFilterMode.None;
        AsyncPopulator = Populate;
        MinimumPrefixLength = 0;
        IsTextCompletionEnabled = false;
        PlaceholderText = "Produkt suchen …";
        AutomationProperties.SetName(this, "Produkt");
        ValueMemberBinding = new Binding(nameof(Product.Name));
        ItemTemplate = new FuncDataTemplate<Product>((i, _) => Row(Category(i)), false);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ChoicesProperty) SetCurrentValue(ItemsSourceProperty, Choices);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ItemsControl>("PART_SelectingItemsControl") is { } list)
            list.ContainerPrepared += (_, c) =>
                AutomationProperties.SetName(c.Container, list.Items[c.Index] is Product i ? (i.Name + " " + Category(i)).Trim() : "");
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        if (SelectedItem is not null || InDropDown(e.Source as IInputElement)) return;
        SetCurrentValue(ItemsSourceProperty, Filter(Text));
        PopulateComplete();
        SetCurrentValue(IsDropDownOpenProperty, true);
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        if (InDropDown(TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement()) || SelectedItem is not null || Text is null or "" || Choices is null) return;
        var hits = Filter(Text).Take(2).ToList();
        if (hits.Count == 1) SetCurrentValue(SelectedItemProperty, hits[0]);
        else SetCurrentValue(TextProperty, "");
    }

    // A click into the list moves the focus there before it selects: the box is left, but not yet decided.
    bool InDropDown(IInputElement? focused) =>
        focused is Visual v && this.GetVisualDescendants().OfType<Popup>().Any(p => p.Child?.IsVisualAncestorOf(v) == true);

    // What begins with the text comes first: "Pommes" before "Schnitzel mit Pommes".
    List<Product> Filter(string? text) =>
        Choices?.Where(i => Matches(text, i)).OrderBy(i => !i.Name.StartsWith((text ?? "").Trim(), StringComparison.OrdinalIgnoreCase)).ToList() ?? [];

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

    string Category(Product? i) =>
        i is null ? "" : GetCategoryNames(this)?.GetValueOrDefault(i.CategoryId ?? "") ?? "";

    static Control Row(string category)
    {
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        name[!TextBlock.TextProperty] = new Binding(nameof(Product.Name));
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

    bool Matches(string? text, Product i)
    {
        var terms = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var s = i.Name + " " + Category(i);
        return terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

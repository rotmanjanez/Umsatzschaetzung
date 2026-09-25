using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

// Wählt eine Gewerbekennzahl aus den Regeln: tippen filtert nach Kennzahl oder Bezeichnung.
// Frei getippte Kennzahlen nimmt die Box nicht an; eine unbekannte aus einer alten Prüfung
// bleibt stehen, bis eine andere gewählt wird.
public sealed class GewerbeBox : AutoCompleteBox
{
    public static readonly StyledProperty<IReadOnlyList<Gewerbezweig>?> ChoicesProperty =
        AvaloniaProperty.Register<GewerbeBox, IReadOnlyList<Gewerbezweig>?>(nameof(Choices));

    public static readonly StyledProperty<string> KennzahlProperty =
        AvaloniaProperty.Register<GewerbeBox, string>(nameof(Kennzahl), "", defaultBindingMode: BindingMode.TwoWay);

    public IReadOnlyList<Gewerbezweig>? Choices
    {
        get => GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    public string Kennzahl
    {
        get => GetValue(KennzahlProperty);
        set => SetValue(KennzahlProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(AutoCompleteBox);

    bool syncing;

    public GewerbeBox()
    {
        FilterMode = AutoCompleteFilterMode.None;
        AsyncPopulator = (text, _) => Task.FromResult<IEnumerable<object>>(Filter(text));
        MinimumPrefixLength = 0;
        MaxDropDownHeight = 320;
        IsTextCompletionEnabled = false;
        PlaceholderText = "Kennzahl oder Gewerbe suchen …";
        ValueMemberBinding = new Binding(".") { Converter = new FuncValueConverter<Gewerbezweig, string>(Label) };
        ItemTemplate = new FuncDataTemplate<Gewerbezweig>((_, _) => Row(), false);
    }

    public static string Label(Gewerbezweig? g) => g is null ? "" : g.Kennzahl + " " + g.Name;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (syncing) return;
        if (change.Property == ChoicesProperty || change.Property == KennzahlProperty) Show(Kennzahl);
        else if (change.Property == SelectedItemProperty && SelectedItem is Gewerbezweig g) Commit(g.Kennzahl);
    }

    protected override void OnGotFocus(FocusChangedEventArgs e)
    {
        base.OnGotFocus(e);
        SetCurrentValue(ItemsSourceProperty, Filter(""));
        PopulateComplete();
        SetCurrentValue(IsDropDownOpenProperty, true);
    }

    // A click into the list would take the focus from the text field and close the list
    // before the entry is chosen.
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (e.NameScope.Find<ItemsControl>("PART_SelectingItemsControl") is not { } list) return;
        list.Focusable = false;
        list.ContainerPrepared += (_, c) => c.Container.Focusable = false;
    }

    protected override void OnLostFocus(FocusChangedEventArgs e)
    {
        base.OnLostFocus(e);
        var text = Text?.Trim() ?? "";
        if (text == "") Commit("");
        else if (SelectedItem is Gewerbezweig g && text == Label(g)) return;
        else if (Filter(text).Take(2).ToList() is [var only]) Commit(only.Kennzahl);
        Show(Kennzahl);
    }

    void Commit(string kennzahl)
    {
        syncing = true;
        SetCurrentValue(KennzahlProperty, kennzahl);
        syncing = false;
    }

    void Show(string kennzahl)
    {
        syncing = true;
        var hit = Choices?.FirstOrDefault(g => g.Kennzahl == kennzahl);
        SetCurrentValue(SelectedItemProperty, hit);
        SetCurrentValue(TextProperty, hit is null ? kennzahl : Label(hit));
        syncing = false;
    }

    List<Gewerbezweig> Filter(string? text)
    {
        var terms = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return Choices?.Where(g => terms.All(t => Label(g).Contains(t, StringComparison.OrdinalIgnoreCase))).ToList() ?? [];
    }

    static Control Row()
    {
        var kennzahl = new TextBlock { Width = 64, VerticalAlignment = VerticalAlignment.Center };
        kennzahl[!TextBlock.TextProperty] = new Binding(nameof(Gewerbezweig.Kennzahl));
        kennzahl[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        DockPanel.SetDock(kennzahl, Dock.Left);
        var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        name[!TextBlock.TextProperty] = new Binding(nameof(Gewerbezweig.Name));
        return new DockPanel { Children = { kennzahl, name } };
    }
}

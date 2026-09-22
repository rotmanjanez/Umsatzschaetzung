using System.Collections;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;

namespace Umsatzschaetzung.App.Ui;

public sealed class SearchBox : Grid
{
    public static readonly StyledProperty<bool> NoMatchesProperty =
        AvaloniaProperty.Register<SearchBox, bool>(nameof(NoMatches));

    readonly TextBox box = new() { Padding = new Thickness(12, 5, 6, 6) };
    readonly TextBlock hint = new()
    {
        Text = "  Filtern",
        IsHitTestVisible = false,
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    readonly Button clear = new()
    {
        IsVisible = false,
        Focusable = false,
        Cursor = new Cursor(StandardCursorType.Hand),
        Padding = new Thickness(8, 4),
    };
    readonly List<(DataGridCollectionView View, IEnumerable Source)> views = [];

    public SearchBox()
    {
        Width = 220;
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        var icon = new PathIcon { Width = 10, Height = 10 };
        icon[!PathIcon.DataProperty] = new DynamicResourceExtension("ClearIcon");
        clear.Content = icon;
        clear[!ThemeProperty] = new DynamicResourceExtension("IconButton");
        ToolTip.SetTip(clear, "Filter zurücksetzen");
        box.InnerRightContent = clear;
        Children.Add(box);
        Children.Add(hint);
        box.TextChanged += (_, _) =>
        {
            foreach (var v in views) v.View.Refresh();
            Update();
        };
        clear.Click += (_, _) => Reset();
    }

    // Avalonia has no ICollectionView: the filtered view is a separate object, so
    // consumers bind their ItemsSource to View rather than to the source collection.
    public DataGridCollectionView? View => views.Count > 0 ? views[0].View : null;

    public bool NoMatches { get => GetValue(NoMatchesProperty); private set => SetValue(NoMatchesProperty, value); }

    public DataGridCollectionView Attach<T>(IEnumerable<T> items, Func<T, string> text)
    {
        var view = new DataGridCollectionView((IEnumerable)items) { Filter = o => Match(text((T)o)) };
        view.CollectionChanged += (_, _) => Update();
        views.Add((view, (IEnumerable)items));
        return view;
    }

    public void Reset()
    {
        box.Text = "";
        box.Focus();
    }

    void Update()
    {
        var filtering = box.Text is not (null or "");
        hint.IsVisible = !filtering;
        clear.IsVisible = filtering;
        NoMatches = filtering && views.All(v => v.View.Count == 0) && views.Any(v => v.Source.Cast<object>().Any());
    }

    bool Match(string s)
    {
        var terms = (box.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return terms.Length == 0 || terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

// Overlay for a list filtered by a SearchBox: shown only while the filter hides every row.
public sealed class NoResults : StackPanel
{
    public static readonly StyledProperty<SearchBox?> SearchProperty =
        AvaloniaProperty.Register<NoResults, SearchBox?>(nameof(Search));

    public NoResults()
    {
        IsVisible = false;
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment = VerticalAlignment.Center;
        Margin = new Thickness(24);

        var text = new TextBlock { Text = "Keine Treffer", HorizontalAlignment = HorizontalAlignment.Center };
        text[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        var reset = new Button
        {
            Content = "Filter zurücksetzen",
            Margin = new Thickness(0, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        reset.Click += (_, _) => Search?.Reset();
        Children.Add(text);
        Children.Add(reset);
    }

    public SearchBox? Search { get => GetValue(SearchProperty); set => SetValue(SearchProperty, value); }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != SearchProperty || Search is not { } search) return;
        IsVisible = search.NoMatches;
        search.PropertyChanged += (_, a) =>
        {
            if (a.Property == SearchBox.NoMatchesProperty) IsVisible = search.NoMatches;
        };
    }
}

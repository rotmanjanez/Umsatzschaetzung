using System.Collections;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Threading;

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
    readonly DispatcherTimer typing = new() { Interval = TimeSpan.FromMilliseconds(150) };
    string[] terms = [];

    public SearchBox()
    {
        Width = 220;
        AutomationProperties.SetName(this, "Filtern");
        Accessible.Forward(this, box, id: true);
        AutomationProperties.SetName(clear, "Filter löschen");
        AutomationProperties.SetAutomationId(clear, "SearchClear");
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        var icon = new PathIcon { Width = 10, Height = 10 };
        icon[!PathIcon.DataProperty] = new DynamicResourceExtension("ClearIcon");
        clear.Content = icon;
        clear[!ThemeProperty] = new DynamicResourceExtension("IconButton");
        ToolTip.SetTip(clear, "Filter zurücksetzen");
        box.InnerRightContent = clear;
        Children.Add(box);
        Children.Add(hint);
        // The lists are filtered once typing pauses, not again for every key on the way.
        box.TextChanged += (_, _) =>
        {
            Hint();
            typing.Stop();
            typing.Start();
        };
        typing.Tick += (_, _) => Apply();
        clear.Click += (_, _) => Reset();
        box.LostFocus += (_, _) => Flush();
        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Tab or Key.Up or Key.Down) Flush();
            if (e.Key != Key.Escape || box.Text is null or "") return;
            e.Handled = true;
            Reset();
        };
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new Peer(this);

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
        Apply();
        box.Focus();
    }

    // A key that moves on to the list finds it filtered by what was typed.
    void Flush()
    {
        if (typing.IsEnabled) Apply();
    }

    void Apply()
    {
        typing.Stop();
        terms = (box.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var v in views) v.View.Refresh();
        Update();
    }

    void Hint()
    {
        var filtering = box.Text is not (null or "");
        hint.IsVisible = !filtering;
        clear.IsVisible = filtering;
    }

    void Update() =>
        NoMatches = terms.Length > 0 && views.All(v => v.View.Count == 0) && views.Any(v => v.Source.Cast<object>().Any());

    bool Match(string s) => terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));

    sealed class Peer(SearchBox owner) : NoneAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() =>
            base.GetChildrenCore()?.Where(p => p is not ControlAutomationPeer { Owner: TextBlock }).ToList();
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
        AutomationProperties.SetAutomationId(reset, "SearchReset");
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
            if (a.Property != SearchBox.NoMatchesProperty) return;
            IsVisible = search.NoMatches;
            if (IsVisible) Accessible.Announce(search, "Keine Treffer");
        };
    }
}

using System.Collections;
using Avalonia;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Layout;

namespace Umsatzschätzung.App.Ui;

public sealed class SearchBox : Grid
{
    readonly TextBox box = new();
    readonly TextBlock hint = new()
    {
        Text = "  Filtern",
        IsHitTestVisible = false,
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    Func<object, string> text = _ => "";

    public SearchBox()
    {
        Width = 220;
        hint[!TextBlock.ForegroundProperty] = new DynamicResourceExtension("SystemControlForegroundBaseMediumBrush");
        Children.Add(box);
        Children.Add(hint);
        box.TextChanged += (_, _) =>
        {
            hint.IsVisible = box.Text is null or "";
            View?.Refresh();
        };
    }

    // Avalonia has no ICollectionView: the filtered view is a separate object, so
    // consumers bind their ItemsSource to View rather than to the source collection.
    public DataGridCollectionView? View { get; private set; }

    public void Attach<T>(IEnumerable<T> items, Func<T, string> text)
    {
        this.text = o => text((T)o);
        View = new DataGridCollectionView((IEnumerable)items) { Filter = Match };
    }

    bool Match(object item)
    {
        var terms = (box.Text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return true;
        var s = text(item);
        return terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

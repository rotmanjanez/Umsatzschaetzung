using System.Collections;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace Umsatzschätzung.App.Ui;

public sealed class SearchBox : Grid
{
    readonly TextBox box = new();
    readonly TextBlock hint = new()
    {
        Text = "  Filtern",
        FontFamily = new("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI"),
        IsHitTestVisible = false,
        Margin = new Thickness(12, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    ICollectionView? view;
    Func<object, string> text = _ => "";

    public SearchBox()
    {
        Width = 220;
        hint.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorSecondaryBrush");
        Children.Add(box);
        Children.Add(hint);
        box.TextChanged += (_, _) =>
        {
            hint.Visibility = box.Text == "" ? Visibility.Visible : Visibility.Collapsed;
            view?.Refresh();
        };
    }

    public void Attach<T>(IEnumerable<T> items, Func<T, string> text)
    {
        this.text = o => text((T)o);
        view = CollectionViewSource.GetDefaultView((IEnumerable)items);
        view.Filter = Match;
    }

    bool Match(object item)
    {
        var terms = box.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return true;
        var s = text(item);
        return terms.All(t => s.Contains(t, StringComparison.OrdinalIgnoreCase));
    }
}

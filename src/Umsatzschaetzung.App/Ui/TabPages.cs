using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Umsatzschaetzung.App.Ui;

// Takes the place of the one presenter a tab control swaps its pages through: every page stays
// built and only the selected one shows, so going back to a page only draws it again instead of
// building all its rows anew. A page given to its tab later, on its first visit, is taken in then.
public sealed class TabPages : Panel
{
    TabControl? tabs;
    Control?[] pages = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (tabs is not null || TemplatedParent is not TabControl owner) return;
        tabs = owner;
        pages = new Control?[owner.ItemCount];
        for (var i = 0; i < owner.ItemCount; i++)
        {
            if (owner.Items[i] is not TabItem item) continue;
            var tab = i;
            Take(tab, item);
            item.PropertyChanged += (_, a) => { if (a.Property == ContentControl.ContentProperty) Take(tab, item); };
        }
        owner.PropertyChanged += (_, a) => { if (a.Property == SelectingItemsControl.SelectedIndexProperty) Show(); };
    }

    void Take(int tab, TabItem item)
    {
        if (item.Content is not Control page) return;
        item.Content = null;
        pages[tab] = page;
        page.IsVisible = tab == tabs!.SelectedIndex;
        Children.Add(page);
    }

    void Show()
    {
        for (var i = 0; i < pages.Length; i++)
            if (pages[i] is { } page) page.IsVisible = i == tabs!.SelectedIndex;
    }
}

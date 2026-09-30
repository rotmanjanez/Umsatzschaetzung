using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Umsatzschaetzung.App.Ui;

// Takes the place of the one presenter a tab control swaps its pages through: every page stays
// built and only the selected one shows, so going back to a page only draws it again instead of
// building all its rows anew.
public sealed class TabPages : Panel
{
    TabControl? tabs;
    readonly List<Control?> pages = [];

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (tabs is not null || TemplatedParent is not TabControl owner) return;
        tabs = owner;
        for (var i = 0; i < owner.ItemCount; i++)
        {
            var page = owner.Items[i] is TabItem { Content: Control content } item ? content : null;
            pages.Add(page);
            if (page is null) continue;
            ((TabItem)owner.Items[i]!).Content = null;
            Children.Add(page);
        }
        owner.PropertyChanged += (_, a) => { if (a.Property == SelectingItemsControl.SelectedIndexProperty) Show(); };
        Show();
    }

    void Show()
    {
        for (var i = 0; i < pages.Count; i++)
            if (pages[i] is { } page) page.IsVisible = i == tabs!.SelectedIndex;
    }
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Umsatzschaetzung.App.Ui;

// The editor gets a frame of its own: the positions and the scan sit side by side, both with the
// full height, and the list behind stays a list.
public partial class InvoicePane : UserControl
{
    readonly InvoiceView view;

    InvoicePane(InvoiceView editor)
    {
        InitializeComponent();
        view = editor;
        Body.Content = view;
    }

    public static Frame Open(Control owner, InvoiceView editor, string title)
    {
        var pane = new InvoicePane(editor);
        var frame = Frame.For(pane, title, 1500, 940, 900, 600, fit: true);
        editor.Enter();
        Help.OnF1(frame.Input, () => editor.Topic);
        History.Keys(frame.Input, editor.Move);
        editor.Session.Anchor(frame, pane.ErrorBanner, pane.ErrorText);
        // The view outlives the frame when its review is unfinished, so it is handed back first.
        frame.Closed += () => pane.Body.Content = null;
        frame.Opening += () => editor.SupplierBox.Focus();
        frame.Show(owner);
        return frame;
    }

    void DismissError(object? sender, RoutedEventArgs e) => view.Session.Error = "";
}

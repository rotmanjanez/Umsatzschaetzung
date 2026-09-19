using Avalonia.Controls;

namespace Umsatzschaetzung.App.Ui;

// The editor gets a window of its own: the positions and the scan sit side by side, both with the
// full height, and the list behind stays a list.
public partial class InvoiceWindow : Window
{
    readonly InvoiceView view;

    public InvoiceWindow(InvoiceView editor, string title)
    {
        InitializeComponent();
        view = editor;
        Title = title;
        Body.Content = view;
        view.Enter();
        Opened += (_, _) => Fit();
        // The view outlives the window when its review is unfinished, so it is handed back first.
        Closed += (_, _) => Body.Content = null;
    }

    void Fit()
    {
        if (Screens.ScreenFromWindow(this) is not { } screen) return;
        var room = screen.WorkingArea.Size.ToSize(screen.Scaling);
        var w = Math.Min(Width, room.Width - 80);
        var h = Math.Min(Height, room.Height - 80);
        if (w >= Width && h >= Height) return;
        Width = Math.Max(w, MinWidth);
        Height = Math.Max(h, MinHeight);
        Position = new Avalonia.PixelPoint(
            screen.WorkingArea.X + (int)((room.Width - Width) / 2 * screen.Scaling),
            screen.WorkingArea.Y + (int)((room.Height - Height) / 2 * screen.Scaling));
    }
}

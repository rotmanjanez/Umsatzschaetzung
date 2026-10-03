using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

namespace Umsatzschaetzung.App.Ui;

// A view with a title of its own: a window on the desktop, a sheet over the program in the browser.
// A width or height left out follows the content, and a frame sized in both can be resized.
public abstract class Frame
{
    static readonly AttachedProperty<Frame?> OfProperty =
        AvaloniaProperty.RegisterAttached<Frame, Control, Frame?>("Of", inherits: true);

    public static Frame? Of(Control view) => view.GetValue(OfProperty);

    // Shown by the caller once it has hooked up what the frame should tell it.
    public static Frame For(Control view, string title, double width = double.NaN, double height = double.NaN,
        double minWidth = 0, double minHeight = 0, bool fit = false) =>
        Application.Current?.ApplicationLifetime is ISingleViewApplicationLifetime { MainView: ShellView shell }
            ? shell.Sheets.For(view, title, width, height)
            : new WindowFrame(view, title, width, height, minWidth, minHeight, fit);

    protected Frame(Control view) => view.SetValue(OfProperty, this);

    internal static Control Landmark(Control view)
    {
        AutomationProperties.SetAccessibilityView(view, AccessibilityView.Control);
        AutomationProperties.SetLandmarkType(view, AutomationLandmarkType.Main);
        return view;
    }

    public abstract string Title { get; set; }

    // Where the frame's keys pass first, before whatever has the focus in it.
    public abstract Control Input { get; }

    public event Action? Activated, Closed;

    // Asked before the frame closes; false keeps it open.
    public Func<Task<bool>>? Closing { get; set; }

    // Before the frame shows, so that a screen reader finds the focus with the frame and reads it once.
    public event Action? Opening;

    // An owner of null opens it on its own; a modal frame shuts off its owner until it closes.
    public abstract void Show(Control? owner, bool modal = false);

    public abstract void Activate();

    public abstract void Close();

    protected void RaiseActivated() => Activated?.Invoke();

    protected void RaiseClosed() => Closed?.Invoke();

    protected void RaiseOpening() => Opening?.Invoke();
}

public sealed class WindowFrame : Frame
{
    IInputElement? before;
    Task<bool>? asked;

    public WindowFrame(Control view, string title, double width = double.NaN, double height = double.NaN,
        double minWidth = 0, double minHeight = 0, bool fit = false) : base(view)
    {
        var sized = !double.IsNaN(width) && !double.IsNaN(height);
        Window = new Window
        {
            Content = Landmark(view),
            Title = title,
            Width = width,
            Height = height,
            MinWidth = minWidth,
            MinHeight = minHeight,
            SizeToContent = (double.IsNaN(width) ? SizeToContent.Width : SizeToContent.Manual) | (double.IsNaN(height) ? SizeToContent.Height : SizeToContent.Manual),
            CanResize = sized,
            ShowInTaskbar = sized,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
        };
        Window.Activated += (_, _) => RaiseActivated();
        Window.Closing += async (_, e) =>
        {
            if (Closing is not { } ask || asked is { IsCompletedSuccessfully: true, Result: true }) return;
            e.Cancel = true;
            if (asked is { IsCompleted: false }) return;
            if (await (asked = ask())) Window.Close();
        };
        Window.Closed += (_, _) =>
        {
            RaiseClosed();
            Restore();
        };
        Window.LayoutUpdated += Enter;
        if (fit) Window.Opened += (_, _) => Fit();
        Window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (!Closes(e)) return;
            e.Handled = true;
            Window.Close();
        });
        AppMenu.Attach(Window);
    }

    static bool Closes(KeyEventArgs e) => (e.Key, e.KeyModifiers) switch
    {
        (Key.Escape, KeyModifiers.None) => true,
        (Key.W, var m) => m == (OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control),
        (Key.F4, KeyModifiers.Control) => true,
        _ => false,
    };

    // Before the program has a window, the lifetime shows this one itself.
    public Window Window { get; }

    public override string Title { get => Window.Title ?? ""; set => Window.Title = value; }

    public override Control Input => Window;

    public override void Activate() => Window.Activate();

    public override void Close() => Window.Close();

    public override void Show(Control? owner, bool modal = false)
    {
        var window = owner is null ? null : TopLevel.GetTopLevel(owner) as Window;
        before = window?.FocusManager?.GetFocusedElement();
        if (window is null) Window.Show();
        else if (modal) _ = Window.ShowDialog(window);
        else Window.Show(window);
    }

    // Laid out but not yet on screen. A view that has not taken the focus itself hands it to its first field or button.
    void Enter(object? sender, EventArgs e)
    {
        if (!Window.IsVisible || !Window.IsArrangeValid) return;
        Window.LayoutUpdated -= Enter;
        RaiseOpening();
        if (!Window.IsKeyboardFocusWithin) FocusManager.FindFirstFocusableElement(Window)?.Focus();
    }

    // Back to where the owner was when this window opened, unless the owner has moved on since.
    void Restore()
    {
        if (before is Visual element && TopLevel.GetTopLevel(element) is { FocusManager: { } focus } && focus.GetFocusedElement() is null)
            before.Focus();
    }

    void Fit()
    {
        if (Window.Screens.ScreenFromWindow(Window) is not { } screen) return;
        var room = screen.WorkingArea.Size.ToSize(screen.Scaling);
        var w = Math.Min(Window.Width, room.Width - 80);
        var h = Math.Min(Window.Height, room.Height - 80);
        if (w >= Window.Width && h >= Window.Height) return;
        Window.Width = Math.Max(w, Window.MinWidth);
        Window.Height = Math.Max(h, Window.MinHeight);
        Window.Position = new PixelPoint(
            screen.WorkingArea.X + (int)((room.Width - Window.Width) / 2 * screen.Scaling),
            screen.WorkingArea.Y + (int)((room.Height - Window.Height) / 2 * screen.Scaling));
    }
}

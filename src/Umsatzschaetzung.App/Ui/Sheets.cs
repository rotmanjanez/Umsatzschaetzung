using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

// A browser page has no windows: what the desktop opens as one lies over the program as a sheet,
// the newest on top and the others reachable from the strip above. A modal sheet shuts off
// everything beneath it until it closes.
public sealed class Sheets : Grid
{
    readonly List<Sheet> stack = [];
    readonly StackPanel strip = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        Spacing = 4,
        Margin = new Thickness(16, 8, 16, 0),
    };

    public Sheets()
    {
        RowDefinitions = new RowDefinitions("Auto,*");
        Children.Add(strip);
    }

    // What lies beneath the sheets.
    public Control? Below { get; set; }

    internal Frame For(Control view, string title, double width, double height) => new Sheet(this, view, title, width, height);

    void Add(Sheet sheet)
    {
        stack.Add(sheet);
        if (sheet.Backdrop is { } backdrop) Children.Add(backdrop);
        Children.Add(sheet.Chrome);
        strip.Children.Add(sheet.Tab);
        Order();
        sheet.Focus();
    }

    void Remove(Sheet sheet)
    {
        stack.Remove(sheet);
        if (sheet.Backdrop is { } backdrop) Children.Remove(backdrop);
        Children.Remove(sheet.Chrome);
        strip.Children.Remove(sheet.Tab);
        Order();
    }

    // Once the closed sheet has told its owner, the one now on top has the keys again.
    void Closed(Sheet sheet)
    {
        if (stack.Count > 0) stack[^1].Focus();
        else sheet.Restore();
    }

    void Raise(Sheet sheet)
    {
        if (!stack.Contains(sheet)) return;
        if (stack[^1] == sheet)
        {
            sheet.Tab.IsChecked = true;
            return;
        }
        stack.Remove(sheet);
        stack.Add(sheet);
        Order();
        sheet.Focus();
    }

    // Beneath the newest modal sheet nothing takes a click or a key, and the strip waits for it too.
    void Order()
    {
        var modal = stack.FindLastIndex(s => s.Backdrop is not null);
        for (var i = 0; i < stack.Count; i++)
        {
            var s = stack[i];
            if (s.Backdrop is { } backdrop) backdrop.ZIndex = 2 * i;
            s.Chrome.ZIndex = 2 * i + 1;
            s.Chrome.IsEnabled = i >= modal;
            s.Tab.IsChecked = i == stack.Count - 1;
        }
        strip.IsVisible = modal < 0 && strip.Children.Count > 1;
        if (Below is not { } below) return;
        below.IsEnabled = modal < 0;
        // A native control lies above everything drawn, the sheets too.
        foreach (var host in below.GetVisualDescendants().OfType<NativeControlHost>()) host.IsVisible = stack.Count == 0;
    }

    static T Themed<T>(T control, string theme) where T : StyledElement
    {
        if (Application.Current?.TryFindResource(theme, out var found) == true && found is ControlTheme t) control.Theme = t;
        return control;
    }

    sealed class Sheet : Frame
    {
        readonly Sheets sheets;
        readonly TextBlock title;
        IInputElement? before;

        public Sheet(Sheets sheets, Control view, string title, double width, double height) : base(view)
        {
            this.sheets = sheets;
            this.title = Themed(new TextBlock { Text = title }, "SheetTitle");
            Tab = Themed(new ToggleButton { Content = title }, "SheetTab");
            Tab.Click += (_, _) => Activate();
            var close = Themed(new Button { Content = new PathIcon { Data = Geometry("ClearIcon"), Width = 10, Height = 10 } }, "IconButton");
            ToolTip.SetTip(close, "Schließen (Esc)");
            close.Click += (_, _) => Close();
            DockPanel.SetDock(close, Dock.Right);
            var bar = Themed(new Border { Child = new DockPanel { Children = { close, this.title } } }, "SheetBar");
            DockPanel.SetDock(bar, Dock.Top);
            Chrome = Themed(new Border
            {
                Child = new DockPanel { Children = { bar, view } },
                Focusable = true,
                MaxWidth = double.IsNaN(width) ? double.PositiveInfinity : width,
                MaxHeight = double.IsNaN(height) ? double.PositiveInfinity : height,
                HorizontalAlignment = double.IsNaN(width) ? HorizontalAlignment.Center : HorizontalAlignment.Stretch,
                VerticalAlignment = double.IsNaN(height) ? VerticalAlignment.Center : VerticalAlignment.Stretch,
            }, "Sheet");
            SetRow(Chrome, 1);
            Chrome.AddHandler(InputElement.PointerPressedEvent, (_, _) => sheets.Raise(this), RoutingStrategies.Tunnel, handledEventsToo: true);
            Chrome.KeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
        }

        public Border Chrome { get; }

        public ToggleButton Tab { get; }

        public Rectangle? Backdrop { get; private set; }

        public override string Title
        {
            get => title.Text ?? "";
            set
            {
                title.Text = value;
                Tab.Content = value;
            }
        }

        public override Control Input => Chrome;

        public override void Show(Control? owner, bool modal = false)
        {
            if (modal)
            {
                Backdrop = Themed(new Rectangle(), "SheetBackdrop");
                SetRowSpan(Backdrop, 2);
            }
            before = TopLevel.GetTopLevel(sheets)?.FocusManager?.GetFocusedElement();
            sheets.Add(this);
        }

        public override void Activate() => sheets.Raise(this);

        public override void Close()
        {
            if (!sheets.stack.Contains(this)) return;
            sheets.Remove(this);
            RaiseClosed();
            sheets.Closed(this);
        }

        internal void Focus()
        {
            Chrome.Focus();
            RaiseActivated();
        }

        // Back to where the program was when the first sheet opened over it.
        internal void Restore()
        {
            if (before is Visual element && TopLevel.GetTopLevel(element) is not null) before.Focus();
        }

        static Geometry? Geometry(string key) =>
            Application.Current?.TryFindResource(key, out var found) == true ? found as Geometry : null;
    }
}

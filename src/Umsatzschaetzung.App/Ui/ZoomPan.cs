using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace Umsatzschaetzung.App.Ui;

public static class ZoomPan
{
    public const double Min = 0.1;
    public const double Max = 8;
    public const double Step = 1.25;

    const string KeysHelp = "Plus und Minus zoomen, 0 passt an die Breite an, die Pfeiltasten verschieben den Ausschnitt, Pos1 und Ende springen an den Anfang und das Ende.";

    // Avalonia reports wheel notches, not WPF's 120ths.
    const double Notch = 40;

    // Avalonia exposes no system scrollbar metric; the Fluent bar is 16px wide.
    const double ScrollBarWidth = 16;

    // How tall a revealed word has to end up before it is worth looking at.
    const double Legible = 30;

    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, bool>("Enabled", typeof(ZoomPan));

    public static readonly AttachedProperty<double> ZoomProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("Zoom", typeof(ZoomPan), 1.0);

    static readonly AttachedProperty<double> FitProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, double>("Fit", typeof(ZoomPan), 1.0);

    static readonly AttachedProperty<State?> StateProperty =
        AvaloniaProperty.RegisterAttached<ScrollViewer, State?>("State", typeof(ZoomPan));

    static ZoomPan()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, e) =>
        {
            if (viewer.GetValue(StateProperty) is { } old) old.Detach();
            viewer.SetValue(StateProperty, e.GetNewValue<bool>() ? new State(viewer) : null);
        });
        ZoomProperty.Changed.AddClassHandler<ScrollViewer>((viewer, _) => Reapply(viewer));
        FitProperty.Changed.AddClassHandler<ScrollViewer>((viewer, _) => Reapply(viewer));
    }

    public static void SetEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(EnabledProperty, value);

    public static bool GetEnabled(ScrollViewer viewer) => viewer.GetValue(EnabledProperty);

    // Avalonia has no coercion callback, so the range lives here.
    public static void SetZoom(ScrollViewer viewer, double value) => viewer.SetValue(ZoomProperty, Math.Clamp(value, Min, Max));

    public static double GetZoom(ScrollViewer viewer) => viewer.GetValue(ZoomProperty);

    static double GetFit(ScrollViewer viewer) => viewer.GetValue(FitProperty);

    public static void ZoomBy(ScrollViewer viewer, double factor)
    {
        ZoomAt(viewer, GetZoom(viewer) * factor, new Point(viewer.Viewport.Width / 2, viewer.Viewport.Height / 2));
        Speak(viewer);
    }

    public static void FitWidth(ScrollViewer viewer)
    {
        if (WidthRatio(viewer) is not { } ratio) return;
        SetZoom(viewer, ratio / GetFit(viewer));
        viewer.ScrollToHome();
        Speak(viewer);
    }

    static void Speak(ScrollViewer viewer) => Accessible.Announce(viewer, $"Zoom {GetZoom(viewer):P0}");

    static double? WidthRatio(ScrollViewer viewer)
    {
        if (Content(viewer) is not { Bounds.Width: > 0 } content) return null;
        var room = viewer.Bounds.Width - viewer.Padding.Left - viewer.Padding.Right - ScrollBarWidth;
        return room > 0 ? room / content.Bounds.Width : null;
    }

    // Fitted into a narrow pane a page sits at a zoom where a single word is a few pixels tall and
    // scrolling to it moves nothing: zoom in on it first, and then it is worth centring.
    public static void Reveal(ScrollViewer viewer, Rect box)
    {
        var enlarged = Enlarge(viewer, box);
        var zoom = Scale(viewer);
        var scaled = new Rect(box.X * zoom, box.Y * zoom, box.Width * zoom, box.Height * zoom);
        if (enlarged)
        {
            viewer.UpdateLayout();
            viewer.Offset = new Vector(
                scaled.Center.X - viewer.Viewport.Width / 2,
                scaled.Center.Y - viewer.Viewport.Height / 2);
            return;
        }
        viewer.Offset = new Vector(
            Clamp(viewer.Offset.X, scaled.Left, scaled.Right, viewer.Viewport.Width),
            Clamp(viewer.Offset.Y, scaled.Top, scaled.Bottom, viewer.Viewport.Height));
    }

    static bool Enlarge(ScrollViewer viewer, Rect box)
    {
        if (box.Height <= 0 || box.Height * Scale(viewer) >= Legible) return false;
        var before = GetZoom(viewer);
        SetZoom(viewer, Legible / box.Height / GetFit(viewer));
        return GetZoom(viewer) != before;
    }

    static double Clamp(double offset, double near, double far, double viewport)
    {
        if (far - near >= viewport || near < offset) return near - viewport / 4;
        return far > offset + viewport ? far - viewport * 3 / 4 : offset;
    }

    static double Scale(ScrollViewer viewer) => GetZoom(viewer) * GetFit(viewer);

    // WPF scaled the content itself; Avalonia needs a LayoutTransformControl between viewer and content.
    static LayoutTransformControl? Host(ScrollViewer viewer) => viewer.Content as LayoutTransformControl;

    static Control? Content(ScrollViewer viewer) => Host(viewer)?.Child;

    static void Reapply(ScrollViewer viewer)
    {
        if (viewer.GetValue(StateProperty) is { } state) state.Apply();
    }

    static void ZoomAt(ScrollViewer viewer, double zoom, Point pivot)
    {
        var before = GetZoom(viewer);
        SetZoom(viewer, zoom);
        var factor = GetZoom(viewer) / before;
        if (factor == 1) return;
        viewer.UpdateLayout();
        viewer.Offset = new Vector((viewer.Offset.X + pivot.X) * factor - pivot.X, (viewer.Offset.Y + pivot.Y) * factor - pivot.Y);
    }

    sealed class State
    {
        readonly ScrollViewer viewer;
        Point grab;
        Vector origin;
        bool panning;

        public State(ScrollViewer viewer)
        {
            this.viewer = viewer;
            viewer.AddHandler(InputElement.PointerWheelChangedEvent, Wheel, RoutingStrategies.Tunnel);
            viewer.AddHandler(InputElement.PointerPressedEvent, Down, RoutingStrategies.Tunnel);
            viewer.AddHandler(InputElement.PointerMovedEvent, Move, RoutingStrategies.Tunnel);
            viewer.AddHandler(InputElement.PointerReleasedEvent, Up, RoutingStrategies.Tunnel);
            viewer.KeyDown += Pressed;
            viewer.Focusable = true;
            Accessible.Install(viewer, v => new ScanPeer(v));
            if (Application.Current?.TryFindResource("FocusInset", out var ring) == true) viewer.FocusAdorner = ring as ITemplate<Control>;
            AutomationProperties.SetHelpText(viewer, KeysHelp);
            viewer.SizeChanged += Refit;
            viewer.ScrollChanged += Scrolled;
            viewer.Loaded += Loaded;
            if (viewer.IsLoaded) Loaded(viewer, new RoutedEventArgs());
        }

        public void Detach()
        {
            viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, Wheel);
            viewer.RemoveHandler(InputElement.PointerPressedEvent, Down);
            viewer.RemoveHandler(InputElement.PointerMovedEvent, Move);
            viewer.RemoveHandler(InputElement.PointerReleasedEvent, Up);
            viewer.KeyDown -= Pressed;
            viewer.SizeChanged -= Refit;
            viewer.ScrollChanged -= Scrolled;
            viewer.Loaded -= Loaded;
            if (Host(viewer) is { } host) host.LayoutTransform = null;
        }

        // A fresh transform each time: LayoutTransformControl re-measures on the property change, not on mutation.
        public void Apply()
        {
            if (Host(viewer) is not { } host) return;
            var scale = Scale(viewer);
            host.LayoutTransform = new ScaleTransform(scale, scale);
        }

        void Scrolled(object? sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentDelta.X != 0) Refit(sender, e);
        }

        void Refit(object? sender, EventArgs e)
        {
            if (WidthRatio(viewer) is { } ratio) viewer.SetValue(FitProperty, Math.Min(1, ratio));
        }

        void Loaded(object? sender, RoutedEventArgs e)
        {
            Refit(viewer, EventArgs.Empty);
            Apply();
        }

        void Wheel(object? sender, PointerWheelEventArgs e)
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                ZoomAt(viewer, GetZoom(viewer) * Math.Pow(Step, e.Delta.Y), e.GetPosition(viewer));
                e.Handled = true;
                return;
            }
            var sideways = e.Delta.X != 0 ? e.Delta.X : e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? e.Delta.Y : 0;
            if (sideways == 0) return;
            viewer.Offset = new Vector(viewer.Offset.X - sideways * Notch, viewer.Offset.Y);
            e.Handled = true;
        }

        void Down(object? sender, PointerPressedEventArgs e)
        {
            var buttons = e.GetCurrentPoint(viewer).Properties;
            if (buttons.IsRightButtonPressed) return;
            if (buttons.IsLeftButtonPressed && e.Source is Visual source && InText(source)) return;
            viewer.Focus(NavigationMethod.Pointer);
            grab = e.GetPosition(viewer);
            origin = viewer.Offset;
            panning = true;
            viewer.Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(viewer);
        }

        void Move(object? sender, PointerEventArgs e)
        {
            if (!panning) return;
            var delta = e.GetPosition(viewer) - grab;
            viewer.Offset = new Vector(origin.X - delta.X, origin.Y - delta.Y);
        }

        void Up(object? sender, PointerReleasedEventArgs e)
        {
            if (!panning) return;
            panning = false;
            viewer.Cursor = null;
            e.Pointer.Capture(null);
        }

        void Pressed(object? sender, KeyEventArgs e)
        {
            var inText = e.Source is Visual source && InText(source);
            if (inText && !e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            switch (e.Key)
            {
                case Key.OemPlus or Key.Add: ZoomBy(viewer, Step); break;
                case Key.OemMinus or Key.Subtract: ZoomBy(viewer, 1 / Step); break;
                case Key.D0 or Key.NumPad0: FitWidth(viewer); break;
                case Key.Left when !inText: viewer.LineLeft(); break;
                case Key.Right when !inText: viewer.LineRight(); break;
                case Key.Up when !inText: viewer.LineUp(); break;
                case Key.Down when !inText: viewer.LineDown(); break;
                case Key.Home when !inText: viewer.ScrollToHome(); break;
                case Key.End when !inText: viewer.ScrollToEnd(); break;
                default: return;
            }
            e.Handled = true;
        }

        static bool InText(Visual source) => source is TextBox || source.GetVisualAncestors().Any(v => v is TextBox);
    }

    // The bars come and go with the zoom, and each time VoiceOver would count the viewer's parts
    // as updated: only the scan is its content, the bars stay reachable as the viewer's own.
    sealed class ScanPeer(ScrollViewer owner) : ScrollViewerAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer>? GetChildrenCore() =>
            Owner.Presenter is { } presenter ? [GetOrCreate(presenter)] : null;
    }
}

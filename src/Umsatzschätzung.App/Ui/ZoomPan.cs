using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace Umsatzschätzung.App.Ui;

public static class ZoomPan
{
    public const double Min = 0.1;
    public const double Max = 8;
    public const double Step = 1.25;

    const int WmMouseHWheel = 0x020E;

    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(ZoomPan), new PropertyMetadata(false, EnabledChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.RegisterAttached(
        "Zoom", typeof(double), typeof(ZoomPan), new PropertyMetadata(1.0, ZoomChanged, CoerceZoom));

    static readonly DependencyProperty FitProperty = DependencyProperty.RegisterAttached(
        "Fit", typeof(double), typeof(ZoomPan), new PropertyMetadata(1.0, FitChanged));

    static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(ZoomPan));

    public static void SetEnabled(ScrollViewer viewer, bool value) => viewer.SetValue(EnabledProperty, value);

    public static bool GetEnabled(ScrollViewer viewer) => (bool)viewer.GetValue(EnabledProperty);

    public static void SetZoom(ScrollViewer viewer, double value) => viewer.SetValue(ZoomProperty, value);

    public static double GetZoom(ScrollViewer viewer) => (double)viewer.GetValue(ZoomProperty);

    static double GetFit(ScrollViewer viewer) => (double)viewer.GetValue(FitProperty);

    public static void ZoomBy(ScrollViewer viewer, double factor) =>
        ZoomAt(viewer, GetZoom(viewer) * factor, new Point(viewer.ViewportWidth / 2, viewer.ViewportHeight / 2));

    public static void FitWidth(ScrollViewer viewer)
    {
        if (WidthRatio(viewer) is not { } ratio) return;
        SetZoom(viewer, ratio / GetFit(viewer));
        viewer.ScrollToHome();
    }

    static double? WidthRatio(ScrollViewer viewer)
    {
        if (Content(viewer) is not { ActualWidth: > 0 } content) return null;
        var room = viewer.ActualWidth - viewer.Padding.Left - viewer.Padding.Right - SystemParameters.VerticalScrollBarWidth;
        return room > 0 ? room / content.ActualWidth : null;
    }

    public static void Reveal(ScrollViewer viewer, Rect box)
    {
        var zoom = Scale(viewer);
        var scaled = new Rect(box.X * zoom, box.Y * zoom, box.Width * zoom, box.Height * zoom);
        viewer.ScrollToHorizontalOffset(Clamp(viewer.HorizontalOffset, scaled.Left, scaled.Right, viewer.ViewportWidth));
        viewer.ScrollToVerticalOffset(Clamp(viewer.VerticalOffset, scaled.Top, scaled.Bottom, viewer.ViewportHeight));
    }

    static double Clamp(double offset, double near, double far, double viewport)
    {
        if (far - near >= viewport || near < offset) return near - viewport / 4;
        return far > offset + viewport ? far - viewport * 3 / 4 : offset;
    }

    static double Scale(ScrollViewer viewer) => GetZoom(viewer) * GetFit(viewer);

    static object CoerceZoom(DependencyObject o, object value) => Math.Clamp((double)value, Min, Max);

    static FrameworkElement? Content(ScrollViewer viewer) => viewer.Content as FrameworkElement;

    static void EnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not ScrollViewer viewer) return;
        if (viewer.GetValue(StateProperty) is State old) old.Detach();
        viewer.SetValue(StateProperty, (bool)e.NewValue ? new State(viewer) : null);
    }

    static void ZoomChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) => Reapply(o);

    static void FitChanged(DependencyObject o, DependencyPropertyChangedEventArgs e) => Reapply(o);

    static void Reapply(DependencyObject o)
    {
        if (o is ScrollViewer viewer && viewer.GetValue(StateProperty) is State state) state.Apply();
    }

    static void ZoomAt(ScrollViewer viewer, double zoom, Point pivot)
    {
        var before = GetZoom(viewer);
        SetZoom(viewer, zoom);
        var factor = GetZoom(viewer) / before;
        if (factor == 1) return;
        viewer.UpdateLayout();
        viewer.ScrollToHorizontalOffset((viewer.HorizontalOffset + pivot.X) * factor - pivot.X);
        viewer.ScrollToVerticalOffset((viewer.VerticalOffset + pivot.Y) * factor - pivot.Y);
    }

    sealed class State
    {
        readonly ScrollViewer viewer;
        readonly ScaleTransform scale = new(1, 1);
        HwndSource? hwnd;
        Point grab;
        Vector origin;
        bool panning;

        public State(ScrollViewer viewer)
        {
            this.viewer = viewer;
            viewer.PreviewMouseWheel += Wheel;
            viewer.PreviewMouseDown += Down;
            viewer.PreviewMouseMove += Move;
            viewer.PreviewMouseUp += Up;
            viewer.SizeChanged += Refit;
            viewer.ScrollChanged += Scrolled;
            viewer.Loaded += Loaded;
            viewer.Unloaded += Unloaded;
            if (viewer.IsLoaded) Loaded(viewer, new RoutedEventArgs());
        }

        public void Detach()
        {
            viewer.PreviewMouseWheel -= Wheel;
            viewer.PreviewMouseDown -= Down;
            viewer.PreviewMouseMove -= Move;
            viewer.PreviewMouseUp -= Up;
            viewer.SizeChanged -= Refit;
            viewer.ScrollChanged -= Scrolled;
            viewer.Loaded -= Loaded;
            viewer.Unloaded -= Unloaded;
            Unloaded(viewer, new RoutedEventArgs());
            if (Content(viewer) is { } content && ReferenceEquals(content.LayoutTransform, scale))
                content.LayoutTransform = Transform.Identity;
        }

        public void Apply()
        {
            if (Content(viewer) is not { } content) return;
            content.LayoutTransform = scale;
            scale.ScaleX = scale.ScaleY = Scale(viewer);
        }

        void Scrolled(object? sender, ScrollChangedEventArgs e)
        {
            if (e.ExtentWidthChange != 0) Refit(sender, e);
        }

        void Refit(object? sender, EventArgs e)
        {
            if (WidthRatio(viewer) is { } ratio) viewer.SetValue(FitProperty, Math.Min(1, ratio));
        }

        void Loaded(object? sender, RoutedEventArgs e)
        {
            Refit(viewer, EventArgs.Empty);
            Apply();
            if (hwnd is null && PresentationSource.FromVisual(viewer) is HwndSource source)
            {
                hwnd = source;
                hwnd.AddHook(Hook);
            }
        }

        void Unloaded(object? sender, RoutedEventArgs e)
        {
            hwnd?.RemoveHook(Hook);
            hwnd = null;
        }

        nint Hook(nint window, int message, nint wParam, nint lParam, ref bool handled)
        {
            if (message != WmMouseHWheel || !viewer.IsMouseOver) return 0;
            viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset + (short)((ulong)wParam >> 16) / 3.0);
            handled = true;
            return 0;
        }

        void Wheel(object sender, MouseWheelEventArgs e)
        {
            var keys = Keyboard.Modifiers;
            if ((keys & ModifierKeys.Control) != 0)
            {
                ZoomAt(viewer, GetZoom(viewer) * Math.Pow(Step, e.Delta / 120.0), e.GetPosition(viewer));
                e.Handled = true;
                return;
            }
            if ((keys & ModifierKeys.Shift) == 0) return;
            viewer.ScrollToHorizontalOffset(viewer.HorizontalOffset - e.Delta / 3.0);
            e.Handled = true;
        }

        void Down(object sender, MouseButtonEventArgs e)
        {
            var text = e.ChangedButton == MouseButton.Left && e.OriginalSource is DependencyObject source && InText(source);
            if (e.ChangedButton == MouseButton.Right || text) return;
            grab = e.GetPosition(viewer);
            origin = new Vector(viewer.HorizontalOffset, viewer.VerticalOffset);
            panning = true;
            viewer.Cursor = Cursors.ScrollAll;
            viewer.CaptureMouse();
        }

        void Move(object sender, MouseEventArgs e)
        {
            if (!panning) return;
            var delta = e.GetPosition(viewer) - grab;
            viewer.ScrollToHorizontalOffset(origin.X - delta.X);
            viewer.ScrollToVerticalOffset(origin.Y - delta.Y);
        }

        void Up(object sender, MouseButtonEventArgs e)
        {
            if (!panning) return;
            panning = false;
            viewer.Cursor = null;
            viewer.ReleaseMouseCapture();
        }

        static bool InText(DependencyObject source)
        {
            for (var node = source; node is not null; node = VisualTreeHelper.GetParent(node))
                if (node is TextBoxBase) return true;
            return false;
        }
    }
}

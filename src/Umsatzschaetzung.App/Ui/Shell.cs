using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed class Shell : Window
{
    public Shell(Services service)
    {
        View = new ShellView(service);
        Content = Frame.Landmark(View);
        Title = "Umsatzschätzung";
        (Width, Height, MinWidth, MinHeight) = (1320, 860, 960, 600);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        View.Titled += title => Title = title;
        AppMenu.Attach(this);
        Activated += (_, _) => Session.ActiveWindow = this;
        Closing += async (_, e) =>
        {
            var saved = View.Leave();
            if (saved.IsCompleted) return;
            e.Cancel = true;
            await saved;
            Close();
        };
        Closed += (_, _) => View.Closed();
    }

    public ShellView View { get; }

    internal Session Session => View.Session;

    internal TabControl Tabs => View.Tabs;
}

// A screen reader presses a tab to open it and reads it as checked, but the tab only knows how to be selected.
static class TabItems
{
    public static void Register() =>
        TemplatedControl.TemplateAppliedEvent.AddClassHandler<TabItem>((tab, _) => Peer(tab) ??= new PressablePeer(tab));

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_automationPeer")]
    static extern ref AutomationPeer? Peer(Control control);

    sealed class PressablePeer(TabItem owner) : ListItemAutomationPeer(owner), ISelectionItemProvider, IInvokeProvider, IToggleProvider
    {
        public ToggleState ToggleState => IsSelected ? ToggleState.On : ToggleState.Off;

        public void Toggle() => Select();

        public void Invoke() => Select();

        void ISelectionItemProvider.AddToSelection() => Select();

        void ISelectionItemProvider.RemoveFromSelection() { }

        protected override object? GetProviderCore(Type providerType) =>
            providerType == typeof(IToggleProvider) && !OperatingSystem.IsMacOS() ? null : base.GetProviderCore(providerType);
    }
}

// Windows zeigt die Menüleiste im Fenster, macOS erwartet sie oben am Bildschirm.
static class AppMenu
{
    public static void Attach(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var windows = new NativeMenu
        {
            Items =
            {
                Item("Minimieren", Gesture(Key.M), () => window.WindowState = WindowState.Minimized),
                Item("Zoomen", null, () => window.WindowState = window.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized),
                Item("Regeln", null, () => (Desktop?.MainWindow as Shell)?.Session.ShowRules()),
                new NativeMenuItemSeparator(),
            },
        };
        var listed = windows.Items.Count;
        void List()
        {
            ((NativeMenuItem)windows.Items[1]).IsEnabled = window.CanResize;
            while (windows.Items.Count > listed) windows.Items.RemoveAt(listed);
            foreach (var w in Desktop?.Windows ?? [])
            {
                var item = Item(w.Title ?? "", null, w.Activate);
                (item.ToggleType, item.IsChecked) = (MenuItemToggleType.CheckBox, w == window);
                windows.Items.Add(item);
            }
        }
        window.Activated += (_, _) => List();
        var f1 = new KeyGesture(Key.F1);
        NativeMenu.SetMenu(window, new NativeMenu
        {
            Items =
            {
                Menu("Ablage", Item("Fenster schließen", Gesture(Key.W), window.Close)),
                Menu("Bearbeiten",
                    Pass(window, "Widerrufen", Gesture(Key.Z)),
                    Pass(window, "Wiederholen", Gesture(Key.Z, KeyModifiers.Shift)),
                    new NativeMenuItemSeparator(),
                    Pass(window, "Ausschneiden", Gesture(Key.X)),
                    Pass(window, "Kopieren", Gesture(Key.C)),
                    Pass(window, "Einfügen", Gesture(Key.V)),
                    Pass(window, "Alles auswählen", Gesture(Key.A))),
                new NativeMenuItem("Fenster") { Menu = windows },
                Menu("Hilfe",
                    Item("Hilfe zu dieser Seite", f1, () => { if (!Press(window, f1)) Help.Open(window, Help.Start); }),
                    Item("Handbuch", null, () => Help.Open(window, Help.Start))),
            },
        });
    }

    static IClassicDesktopStyleApplicationLifetime? Desktop => Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;

    static KeyGesture Gesture(Key key, KeyModifiers also = KeyModifiers.None) => new(key, KeyModifiers.Meta | also);

    static NativeMenuItem Menu(string header, params NativeMenuItemBase[] items)
    {
        var menu = new NativeMenu();
        foreach (var item in items) menu.Items.Add(item);
        return new NativeMenuItem(header) { Menu = menu };
    }

    static NativeMenuItem Item(string header, KeyGesture? gesture, Action click)
    {
        var item = new NativeMenuItem(header) { Gesture = gesture };
        item.Click += (_, _) => click();
        return item;
    }

    static NativeMenuItem Pass(Window window, string header, KeyGesture gesture) => Item(header, gesture, () => Press(window, gesture));

    static bool Press(Window window, KeyGesture gesture)
    {
        var target = window.FocusManager?.GetFocusedElement() as Interactive ?? window;
        var e = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers, Source = target };
        target.RaiseEvent(e);
        return e.Handled;
    }
}

using Avalonia.Controls;
using Avalonia.Input;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.App.Ui;

public sealed class Shell : Window
{
    public Shell(IService service)
    {
        View = new ShellView(service);
        Content = View;
        Title = "Umsatzschätzung";
        (Width, Height, MinWidth, MinHeight) = (1320, 860, 960, 600);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        View.Titled += title => Title = title;
        if (OperatingSystem.IsMacOS()) NativeMenu.SetMenu(this, HelpMenu());
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

    // Windows zeigt die Menüleiste im Fenster, macOS erwartet sie oben am Bildschirm.
    NativeMenu HelpMenu()
    {
        var here = new NativeMenuItem("Hilfe zu dieser Seite") { Gesture = new KeyGesture(Key.F1) };
        here.Click += (_, _) => Help.Open(this, View.Topic);
        var manual = new NativeMenuItem("Handbuch");
        manual.Click += (_, _) => Help.Open(this, Help.Start);
        return new NativeMenu { Items = { new NativeMenuItem("Hilfe") { Menu = new NativeMenu { Items = { here, manual } } } } };
    }
}

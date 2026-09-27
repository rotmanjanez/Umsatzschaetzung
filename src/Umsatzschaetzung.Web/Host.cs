using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Web;

// The program as one view in the page element with the given id, over whatever services its head composes.
public static class Host
{
    public static async Task<ShellView> Start(Services service, string element)
    {
        App.App.SingleViewService = () => service;
        await AppBuilder.Configure<App.App>().WithInterFont().StartBrowserAppAsync(element);
        if (Mac()) CommandKey();
        return (ShellView)((ISingleViewApplicationLifetime)Application.Current!.ApplicationLifetime!).MainView!;
    }

    static bool Mac() =>
        JSHost.GlobalThis.GetPropertyAsJSObject("navigator")?.GetPropertyAsString("platform")?.StartsWith("Mac") == true;

    // The browser platform keeps Ctrl for copying, pasting and undoing everywhere; on a Mac it is Cmd.
    static void CommandKey()
    {
        if (Application.Current?.PlatformSettings?.HotkeyConfiguration is not { } keys) return;
        keys.CommandModifiers = KeyModifiers.Meta;
        keys.Copy = Cmd(keys.Copy);
        keys.Cut = Cmd(keys.Cut);
        keys.Paste = Cmd(keys.Paste);
        keys.Undo = Cmd(keys.Undo);
        keys.Redo = Cmd(keys.Redo);
        keys.SelectAll = Cmd(keys.SelectAll);
    }

    static List<KeyGesture> Cmd(List<KeyGesture> gestures) =>
        [.. gestures.Select(g => g.KeyModifiers.HasFlag(KeyModifiers.Control) ? new KeyGesture(g.Key, g.KeyModifiers & ~KeyModifiers.Control | KeyModifiers.Meta) : g)];
}

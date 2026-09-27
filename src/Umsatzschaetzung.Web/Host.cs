using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Platform;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Web;

// The program as one view in the page element with the given id, over whatever services its head composes.
public static partial class Host
{
    public static async Task<ShellView> Start(Services service, string element)
    {
        App.App.SingleViewService = () => service;
        App.App.SingleViewPreview = () => new Iframe();
        App.App.SingleViewPrint = html => Print(WebPages.Seal(html));
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

    [JSImport("globalThis.document.createElement")]
    private static partial JSObject Element(string tag);

    // Same origin so the page may ask it to print, still no script of its own.
    [JSImport("print", "page")]
    private static partial void Print(string html);

    // The Bericht in a frame of the page that runs no script and, by the sealed-in policy, fetches nothing.
    sealed class Iframe : NativeControlHost, IHtmlPreview
    {
        JSObject? frame;
        string html = "";

        public Task<bool> Show(string html, TimeSpan timeout, CancellationToken ct)
        {
            this.html = WebPages.Seal(html);
            frame?.SetProperty("srcdoc", this.html);
            return Task.FromResult(true);
        }

        protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
        {
            frame = Element("iframe");
            frame.SetProperty("sandbox", "");
            frame.GetPropertyAsJSObject("style")?.SetProperty("border", "0");
            frame.SetProperty("srcdoc", html);
            return new JSObjectControlHandle(frame);
        }

        protected override void DestroyNativeControlCore(IPlatformHandle control)
        {
            frame = null;
            base.DestroyNativeControlCore(control);
        }
    }
}

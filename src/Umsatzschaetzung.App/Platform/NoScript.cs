using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Umsatzschaetzung.App.Platform;

// The engine itself refuses page script, beneath the page's policy: a bericht never needs any.
static class NoScript
{
    public static void Apply(IPlatformHandle? handle)
    {
        switch (handle)
        {
            case IWindowsWebView2PlatformHandle windows when OperatingSystem.IsWindows():
                WebView2(windows.CoreWebView2);
                break;
            case IAppleWKWebViewPlatformHandle mac when OperatingSystem.IsMacOS():
                WebKit(mac.WKWebView);
                break;
        }
    }

    static readonly Guid ICoreWebView2 = new("76eceacb-0462-4d94-ac83-423a6793775e");
    const int GetSettings = 3;
    const int PutIsScriptEnabled = 4;
    const int PutAreDevToolsEnabled = 12;
    const int PutAreHostObjectsAllowed = 16;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Getter(nint self, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int Putter(nint self, int value);

    static void WebView2(nint core)
    {
        if (core == 0 || Marshal.QueryInterface(core, in ICoreWebView2, out var view) != 0) return;
        try
        {
            if (Slot<Getter>(view, GetSettings)(view, out var settings) != 0 || settings == 0) return;
            try
            {
                foreach (var put in (int[])[PutIsScriptEnabled, PutAreDevToolsEnabled, PutAreHostObjectsAllowed])
                    Slot<Putter>(settings, put)(settings, 0);
            }
            finally
            {
                Marshal.Release(settings);
            }
        }
        finally
        {
            Marshal.Release(view);
        }
    }

    static T Slot<T>(nint com, int index) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(com), index * nint.Size));

    const string ObjC = "/usr/lib/libobjc.A.dylib";

    // configuration hands out a copy, but the copy shares the live webpage preferences. Only the
    // page's own script goes: WKPreferences.javaScriptEnabled would also stop the script Avalonia
    // evaluates after every navigation.
    static void WebKit(nint webView)
    {
        if (webView == 0) return;
        var preferences = Send(Send(webView, Sel("configuration")), Sel("defaultWebpagePreferences"));
        var setter = Sel("setAllowsContentJavaScript:");
        if (preferences != 0 && Responds(preferences, Sel("respondsToSelector:"), setter) != 0)
            Send(preferences, setter, 0);
    }

    static nint Sel(string name) => sel_registerName(name);

    [DllImport(ObjC)] static extern nint sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(nint self, nint sel, byte value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern byte Responds(nint self, nint sel, nint selector);
}

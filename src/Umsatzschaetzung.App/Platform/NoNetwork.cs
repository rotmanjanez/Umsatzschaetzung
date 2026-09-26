using System.Runtime.InteropServices;
using Avalonia.Platform;

namespace Umsatzschaetzung.App.Platform;

// The engine itself has no network, beneath the page's policy and the navigation guard. WebView2
// sends every request to a proxy nobody answers, loopback included, and a fixed proxy never falls
// back to a direct connection. WebKit's proxies leave loopback alone, so there a content blocker
// refuses every load that is not a file.
static class NoNetwork
{
    public const string Arguments = "--proxy-server=127.0.0.1:9 --proxy-bypass-list=<-loopback>";

    const string Rules =
        """[{"trigger":{"url-filter":".*"},"action":{"type":"block"}},""" +
        """{"trigger":{"url-filter":"^file:"},"action":{"type":"ignore-previous-rules"}},""" +
        """{"trigger":{"url-filter":"^about:"},"action":{"type":"ignore-previous-rules"}},""" +
        """{"trigger":{"url-filter":"^data:"},"action":{"type":"ignore-previous-rules"}}]""";

    static readonly TaskCompletionSource Compiled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    static readonly List<nint> Waiting = [];
    static nint list;

    // A page is shown only once its view refuses the network.
    public static Task Ready => OperatingSystem.IsMacOS() ? Compiled.Task : Task.CompletedTask;

    public static void Apply(IPlatformHandle? handle)
    {
        if (handle is IAppleWKWebViewPlatformHandle mac && OperatingSystem.IsMacOS())
            WebKit(mac.WKWebView);
    }

    static void WebKit(nint webView)
    {
        if (webView == 0) return;
        var controller = Send(Send(webView, Sel("configuration")), Sel("userContentController"));
        if (list != 0)
        {
            Send(controller, Sel("addContentRuleList:"), list);
            return;
        }
        Waiting.Add(Send(controller, Sel("retain")));
        if (Waiting.Count == 1) Compile();
    }

    static unsafe void Compile()
    {
        var block = (Block*)NativeMemory.AllocZeroed((nuint)(sizeof(Block) + sizeof(Descriptor)));
        var descriptor = (Descriptor*)(block + 1);
        descriptor->Size = (ulong)sizeof(Block);
        block->Isa = NativeLibrary.GetExport(NativeLibrary.Load(System), "_NSConcreteGlobalBlock");
        block->Flags = 1 << 28;
        block->Invoke = (nint)(delegate* unmanaged<nint, nint, nint, void>)&Done;
        block->Descriptor = descriptor;
        var store = Send(objc_getClass("WKContentRuleListStore"), Sel("defaultStore"));
        Send(store, Sel("compileContentRuleListForIdentifier:encodedContentRuleList:completionHandler:"),
            Text("umsatzschaetzung-no-network"), Text(Rules), (nint)block);
    }

    [UnmanagedCallersOnly]
    static void Done(nint block, nint compiled, nint error)
    {
        if (compiled == 0)
        {
            Compiled.TrySetException(new InvalidOperationException(
                "the web view could not be cut off from the network: " + Marshal.PtrToStringUTF8(Send(Send(error, Sel("localizedDescription")), Sel("UTF8String")))));
            return;
        }
        list = Send(compiled, Sel("retain"));
        foreach (var controller in Waiting)
        {
            Send(controller, Sel("addContentRuleList:"), list);
            Send(controller, Sel("release"));
        }
        Waiting.Clear();
        Compiled.TrySetResult();
    }

    [StructLayout(LayoutKind.Sequential)]
    unsafe struct Block
    {
        public nint Isa;
        public int Flags;
        public int Reserved;
        public nint Invoke;
        public Descriptor* Descriptor;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct Descriptor
    {
        public ulong Reserved;
        public ulong Size;
    }

    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string System = "/usr/lib/libSystem.B.dylib";

    static nint Sel(string name) => sel_registerName(name);
    static nint Text(string value) => Send(objc_getClass("NSString"), Sel("stringWithUTF8String:"), value);

    [DllImport(ObjC)] static extern nint sel_registerName(string name);
    [DllImport(ObjC)] static extern nint objc_getClass(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel, nint value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(nint self, nint sel, nint a, nint b, nint c);
}

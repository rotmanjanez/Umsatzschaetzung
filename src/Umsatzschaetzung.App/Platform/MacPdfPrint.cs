using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Umsatzschaetzung.App.Platform;

// WKWebView.createPDF snapshots the screen layout onto one tall page; a save-to-file
// NSPrintOperation runs WebKit's print layout, so @media print, @page size and breaks apply.
[SupportedOSPlatform("macos")]
static class MacPdfPrint
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";
    const string AppKit = "/System/Library/Frameworks/AppKit.framework/AppKit";
    const double A4Width = 595.2756;
    const double A4Height = 841.8898;

    delegate void DidRunHandler(nint self, nint sel, nint op, byte success, nint context);

    static readonly DidRunHandler OnDidRunHandler = OnDidRun;
    static readonly Lazy<nint> DelegateClass = new(RegisterDelegate);
    static readonly Lazy<nint> AppKitLibrary = new(() => NativeLibrary.Load(AppKit));
    static readonly nint DidRun = Sel("printOperationDidRun:success:contextInfo:");

    public static async Task<byte[]> Print(nint webView, string folder, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"print-{Guid.NewGuid():N}.pdf");
        var info = Send(Send(Class("NSPrintInfo"), Sel("alloc")), Sel("init"));
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var context = GCHandle.Alloc(done);
        var callback = Send(Send(DelegateClass.Value, Sel("alloc")), Sel("init"));
        try
        {
            Send(info, Sel("setPaperSize:"), new Size(A4Width, A4Height));
            foreach (var margin in (string[])["setTopMargin:", "setBottomMargin:", "setLeftMargin:", "setRightMargin:"])
                Send(info, Sel(margin), 0.0);
            Send(info, Sel("setJobDisposition:"), AppKitString("NSPrintSaveJob"));
            var url = Send(Class("NSURL"), Sel("fileURLWithPath:"), NSString(file));
            Send(Send(info, Sel("dictionary")), Sel("setObject:forKey:"), url, AppKitString("NSPrintJobSavingURL"));

            var op = Send(webView, Sel("printOperationWithPrintInfo:"), info);
            if (op == 0)
                throw new InvalidOperationException("Druckauftrag konnte nicht erstellt werden.");
            Send(op, Sel("setShowsPrintPanel:"), (byte)0);
            Send(op, Sel("setShowsProgressPanel:"), (byte)0);
            Send(op, Sel("runOperationModalForWindow:delegate:didRunSelector:contextInfo:"),
                Send(webView, Sel("window")), callback, DidRun, GCHandle.ToIntPtr(context));

            if (!await done.Task)
                throw new InvalidOperationException("Bericht konnte nicht gedruckt werden.");
            return await File.ReadAllBytesAsync(file, ct);
        }
        finally
        {
            Send(callback, Sel("release"));
            Send(info, Sel("release"));
            context.Free();
            File.Delete(file);
        }
    }

    static nint RegisterDelegate()
    {
        var cls = objc_allocateClassPair(Class("NSObject"), "UmsatzschaetzungPrintDelegate", 0);
        class_addMethod(cls, DidRun, Marshal.GetFunctionPointerForDelegate(OnDidRunHandler), "v@:@c^v");
        objc_registerClassPair(cls);
        return cls;
    }

    static void OnDidRun(nint self, nint sel, nint op, byte success, nint context) =>
        ((TaskCompletionSource<bool>)GCHandle.FromIntPtr(context).Target!).TrySetResult(success != 0);

    static nint AppKitString(string symbol) =>
        Marshal.ReadIntPtr(NativeLibrary.GetExport(AppKitLibrary.Value, symbol));

    static nint NSString(string value) => Send(Class("NSString"), Sel("stringWithUTF8String:"), value);

    static nint Class(string name) => objc_getClass(name);
    static nint Sel(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    readonly record struct Size(double Width, double Height);

    [DllImport(ObjC)] static extern nint objc_getClass(string name);
    [DllImport(ObjC)] static extern nint sel_registerName(string name);
    [DllImport(ObjC)] static extern nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);
    [DllImport(ObjC)] static extern void objc_registerClassPair(nint cls);
    [DllImport(ObjC)] static extern byte class_addMethod(nint cls, nint sel, nint imp, string types);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel, nint a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel, nint a, nint b);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(nint self, nint sel, nint a, nint b, nint c, nint d);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(nint self, nint sel, byte a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(nint self, nint sel, double a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern void Send(nint self, nint sel, Size a);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern nint Send(nint self, nint sel, [MarshalAs(UnmanagedType.LPUTF8Str)] string a);
}

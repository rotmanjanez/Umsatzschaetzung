using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Ausbeute.Service;
using Microsoft.Web.WebView2.Core;

namespace Ausbeute.App.Platform;

public sealed class WebViewPdfPrinter : IPdfPrinter, IDisposable
{
    const int NavigateToStringLimit = 2 * 1024 * 1024;
    const double A4WidthInches = 8.27;
    const double A4HeightInches = 11.69;
    const uint WS_POPUP = 0x80000000;

    readonly string userDataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ausbeute", "webview2");
    readonly SemaphoreSlim gate = new(1, 1);
    IntPtr hwnd;
    CoreWebView2Environment? environment;
    CoreWebView2Controller? controller;

    public async Task<byte[]> Print(string html, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var webView = await WebView(ct);
            await Navigate(webView, html, ct);
            var settings = environment!.CreatePrintSettings();
            settings.PageWidth = A4WidthInches;
            settings.PageHeight = A4HeightInches;
            settings.MarginTop = 0;
            settings.MarginBottom = 0;
            settings.MarginLeft = 0;
            settings.MarginRight = 0;
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            using var pdf = await webView.PrintToPdfStreamAsync(settings).WaitAsync(ct);
            using var buffer = new MemoryStream();
            await pdf.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<CoreWebView2> WebView(CancellationToken ct)
    {
        if (controller is not null)
            return controller.CoreWebView2;
        Directory.CreateDirectory(userDataFolder);
        hwnd = CreateWindowEx(0, "Static", "Ausbeute Druck", WS_POPUP, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
            throw new InvalidOperationException("Verstecktes Druckfenster konnte nicht erzeugt werden.");
        environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder).WaitAsync(ct);
        controller = await environment.CreateCoreWebView2ControllerAsync(hwnd).WaitAsync(ct);
        controller.Bounds = new System.Drawing.Rectangle(0, 0, 794, 1123);
        controller.IsVisible = false;
        return controller.CoreWebView2;
    }

    async Task Navigate(CoreWebView2 webView, string html, CancellationToken ct)
    {
        var done = new TaskCompletionSource<CoreWebView2NavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, CoreWebView2NavigationCompletedEventArgs e) => done.TrySetResult(e);
        webView.NavigationCompleted += Completed;
        string? tempFile = null;
        try
        {
            if (Encoding.UTF8.GetByteCount(html) < NavigateToStringLimit)
            {
                webView.NavigateToString(html);
            }
            else
            {
                tempFile = Path.Combine(userDataFolder, $"print-{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(tempFile, html, Encoding.UTF8, ct);
                webView.Navigate(new Uri(tempFile).AbsoluteUri);
            }
            var result = await done.Task.WaitAsync(ct);
            if (!result.IsSuccess)
                throw new InvalidOperationException($"Bericht konnte nicht geladen werden ({result.WebErrorStatus}).");
        }
        finally
        {
            webView.NavigationCompleted -= Completed;
            if (tempFile is not null)
                File.Delete(tempFile);
        }
    }

    public void Dispose()
    {
        controller?.Close();
        controller = null;
        if (hwnd != IntPtr.Zero)
            DestroyWindow(hwnd);
        hwnd = IntPtr.Zero;
        gate.Dispose();
    }

    [DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DestroyWindow(IntPtr hwnd);
}

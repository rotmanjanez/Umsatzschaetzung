using System.IO;
using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Platform;

public sealed class WebViewPdfPrinter : IPdfPrinter, IDisposable
{
    const int NavigateToStringLimit = 2 * 1024 * 1024;
    const int A4Width = 794;
    const int A4Height = 1123;

    readonly string tempFolder = Path.Combine(AppData.Dir, "print");
    readonly SemaphoreSlim gate = new(1, 1);
    NativeWebDialog? dialog;

    public async Task<byte[]> Print(string html, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(() => Render(html, ct));
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<byte[]> Render(string html, CancellationToken ct)
    {
        var web = Host();
        await Navigate(web, html, ct);
        using var pdf = await PrintToPdf(web).WaitAsync(ct);
        using var buffer = new MemoryStream();
        await pdf.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }

    // The web view's adapter is created by Show(), so the dialog cannot stay hidden;
    // it is parked far off-screen instead and reused for every report.
    NativeWebDialog Host()
    {
        if (dialog is not null) return dialog;
        var web = new NativeWebDialog { Title = "Umsatzschätzung Druck", CanUserResize = false, ShowFocused = false };
        web.Show();
        web.Move(-30000, -30000);
        web.Resize(A4Width, A4Height);
        return dialog = web;
    }

    static Task<Stream> PrintToPdf(NativeWebDialog web)
    {
        if (!OperatingSystem.IsWindows()) return web.PrintToPdfStreamAsync();
        return web.PrintToPdfStreamAsync(new WebViewPrintSettings
        {
            Orientation = WebViewPrintOrientation.Portrait,
            MarginTop = 0,
            MarginBottom = 0,
            MarginLeft = 0,
            MarginRight = 0,
        });
    }

    async Task Navigate(NativeWebDialog web, string html, CancellationToken ct)
    {
        var done = new TaskCompletionSource<WebViewNavigationCompletedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, WebViewNavigationCompletedEventArgs e) => done.TrySetResult(e);
        web.NavigationCompleted += Completed;
        string? tempFile = null;
        try
        {
            if (Encoding.UTF8.GetByteCount(html) < NavigateToStringLimit)
            {
                web.NavigateToString(html, null!);
            }
            else
            {
                Directory.CreateDirectory(tempFolder);
                tempFile = Path.Combine(tempFolder, $"print-{Guid.NewGuid():N}.html");
                await File.WriteAllTextAsync(tempFile, html, Encoding.UTF8, ct);
                web.Navigate(new Uri(tempFile));
            }
            var result = await done.Task.WaitAsync(ct);
            if (!result.IsSuccess)
                throw new InvalidOperationException("Bericht konnte nicht geladen werden.");
        }
        finally
        {
            web.NavigationCompleted -= Completed;
            if (tempFile is not null)
                File.Delete(tempFile);
        }
    }

    public void Dispose()
    {
        var web = dialog;
        dialog = null;
        if (web is not null)
            Dispatcher.UIThread.Invoke(() => { web.Close(); web.Dispose(); });
        gate.Dispose();
    }
}

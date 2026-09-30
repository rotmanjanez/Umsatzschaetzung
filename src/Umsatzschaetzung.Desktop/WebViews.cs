using System.Text;
using Avalonia.Controls;
using Avalonia.Platform;
using Umsatzschaetzung.App.Platform;

namespace Umsatzschaetzung.Desktop;

public interface IHtmlView
{
    event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;
    void Navigate(Uri url);
}

public sealed class HtmlView : NativeWebView, IHtmlView, IHtmlPreview
{
    public HtmlView()
    {
        EnvironmentRequested += (_, e) => WebViews.Isolate(e);
        AdapterCreated += (_, e) =>
        {
            NoScript.Apply(e.TryGetPlatformHandle());
            NoNetwork.Apply(e.TryGetPlatformHandle());
        };
        NavigationStarted += (_, e) => e.Cancel = !WebPages.Ours(e.Request);
        NewWindowRequested += (_, e) => e.Handled = true;
    }

    public Task<bool> Show(string html, TimeSpan timeout, CancellationToken ct) => WebViews.Show(this, html, timeout, ct);
}

public sealed class HtmlDialog : NativeWebDialog, IHtmlView
{
    public HtmlDialog()
    {
        EnvironmentRequested += (_, e) => WebViews.Isolate(e);
        AdapterCreated += (_, e) =>
        {
            NoScript.Apply(e.TryGetPlatformHandle());
            NoNetwork.Apply(e.TryGetPlatformHandle());
        };
        NavigationStarted += (_, e) => e.Cancel = !WebPages.Ours(e.Request);
        NewWindowRequested += (_, e) => e.Handled = true;
    }
}

// Always through a file: WebView2 refuses NavigateToString beyond 2 MB, and a bericht with its
// invoices grows past that.
static class WebViews
{
    internal static void Isolate(WebViewEnvironmentRequestedEventArgs e)
    {
        e.EnableDevTools = false;
        switch (e)
        {
            case WindowsWebView2EnvironmentRequestedEventArgs windows:
                windows.IsInPrivateModeEnabled = true;
                windows.AdditionalBrowserArguments = NoNetwork.Arguments;
                break;
            case AppleWKWebViewEnvironmentRequestedEventArgs mac:
                mac.NonPersistentDataStore = true;
                break;
        }
    }

    public static async Task<bool> Show(this IHtmlView view, string html, TimeSpan timeout, CancellationToken ct)
    {
        await NoNetwork.Ready.WaitAsync(ct);
        Directory.CreateDirectory(WebPages.Folder);
        var file = Path.Combine(WebPages.Folder, $"{Guid.NewGuid()}.html");
        await File.WriteAllTextAsync(file, WebPages.Seal(html), Encoding.UTF8, ct);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            if (WebPages.Of(e.Request, file)) done.TrySetResult(e.IsSuccess);
        }
        view.NavigationCompleted += Completed;
        try
        {
            view.Navigate(new Uri(file));
            return await done.Task.WaitAsync(timeout, ct);
        }
        finally
        {
            view.NavigationCompleted -= Completed;
            File.Delete(file);
        }
    }
}

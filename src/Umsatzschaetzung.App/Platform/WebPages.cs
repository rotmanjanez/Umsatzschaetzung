using System.Text;
using Avalonia.Controls;

namespace Umsatzschaetzung.App.Platform;

public interface IHtmlView
{
    event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;
    void Navigate(Uri url);
}

public sealed class HtmlView : NativeWebView, IHtmlView;

public sealed class HtmlDialog : NativeWebDialog, IHtmlView;

// Always through a file: WebView2 refuses NavigateToString beyond 2 MB, and a bericht with its
// invoices grows past that.
static class WebPages
{
    static readonly string Folder = Path.Combine(AppData.Dir, "pages");

    public static async Task<bool> Show(this IHtmlView view, string html, TimeSpan timeout, CancellationToken ct)
    {
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(file, html, Encoding.UTF8, ct);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, WebViewNavigationCompletedEventArgs e) => done.TrySetResult(e.IsSuccess);
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

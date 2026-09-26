using System.Text;
using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Platform;

namespace Umsatzschaetzung.App.Platform;

public interface IHtmlView
{
    event EventHandler<WebViewNavigationCompletedEventArgs>? NavigationCompleted;
    void Navigate(Uri url);
}

public sealed class HtmlView : NativeWebView, IHtmlView
{
    public HtmlView()
    {
        EnvironmentRequested += (_, e) => WebPages.Isolate(e);
        AdapterCreated += (_, e) =>
        {
            NoScript.Apply(e.TryGetPlatformHandle());
            NoNetwork.Apply(e.TryGetPlatformHandle());
        };
        NavigationStarted += (_, e) => e.Cancel = !WebPages.Ours(e.Request);
        NewWindowRequested += (_, e) => e.Handled = true;
    }
}

public sealed class HtmlDialog : NativeWebDialog, IHtmlView
{
    public HtmlDialog()
    {
        EnvironmentRequested += (_, e) => WebPages.Isolate(e);
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
static partial class WebPages
{
    internal static readonly string Folder = Path.Combine(AppData.Dir, "pages");

    // A page may style itself and embed data: images and fonts, nothing else: no script, no
    // request leaves the file, and a policy of the page's own can only narrow this one.
    internal const string Policy =
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; " +
        "img-src data:; font-src data:; form-action 'none'; base-uri 'none'\">" +
        "<meta http-equiv=\"x-dns-prefetch-control\" content=\"off\">";

    [GeneratedRegex(@"\A﻿?\s*(?:<!--.*?-->\s*)*<!doctype[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Doctype();

    // Behind the doctype, never before it: anything ahead of it drops the page into quirks mode.
    internal static string Seal(string html)
    {
        var at = Doctype().Match(html) is { Success: true } m ? m.Length : 0;
        return string.Concat(html.AsSpan(0, at), Policy, html.AsSpan(at));
    }

    internal static bool Ours(Uri? url) =>
        url is not null && (url.AbsoluteUri == "about:blank"
        || url.IsFile && string.Equals(Path.GetDirectoryName(url.LocalPath)?.Normalize(), Folder.Normalize(), StringComparison.OrdinalIgnoreCase));

    // A navigation the view refused or a newer page cut short completes too, often while the next page loads.
    internal static bool Of(Uri? request, string file) =>
        request is null || request.IsFile && string.Equals(Path.GetFullPath(request.LocalPath), file, StringComparison.OrdinalIgnoreCase);

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

    // A crash while a page is shown leaves its file, and with it the case's data, behind.
    internal static void Clear(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.html"))
        {
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }

    public static async Task<bool> Show(this IHtmlView view, string html, TimeSpan timeout, CancellationToken ct)
    {
        await NoNetwork.Ready.WaitAsync(ct);
        Directory.CreateDirectory(Folder);
        var file = Path.Combine(Folder, $"{Guid.NewGuid()}.html");
        await File.WriteAllTextAsync(file, Seal(html), Encoding.UTF8, ct);
        var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            if (Of(e.Request, file)) done.TrySetResult(e.IsSuccess);
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

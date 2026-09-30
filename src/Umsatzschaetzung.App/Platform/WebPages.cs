using System.Text.RegularExpressions;

namespace Umsatzschaetzung.App.Platform;

// Where the Bericht is previewed: a native web view on the desktop, whatever the host brings in a browser.
public interface IHtmlPreview
{
    Task<bool> Show(string html, TimeSpan timeout, CancellationToken ct);
}

public sealed class NoHtmlPreview : IHtmlPreview
{
    public Task<bool> Show(string html, TimeSpan timeout, CancellationToken ct) => Task.FromResult(false);
}

public static partial class WebPages
{
    public static readonly string Folder = Path.Combine(AppData.Dir, "pages");

    // A page may style itself and embed data: images and fonts, nothing else: no script, no
    // request leaves the file, and a policy of the page's own can only narrow this one.
    public const string Policy =
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; " +
        "img-src data:; font-src data:; form-action 'none'; base-uri 'none'\">" +
        "<meta http-equiv=\"x-dns-prefetch-control\" content=\"off\">";

    [GeneratedRegex(@"\A﻿?\s*(?:<!--.*?-->\s*)*<!doctype[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex Doctype();

    // Behind the doctype, never before it: anything ahead of it drops the page into quirks mode.
    public static string Seal(string html)
    {
        var at = Doctype().Match(html) is { Success: true } m ? m.Length : 0;
        return string.Concat(html.AsSpan(0, at), Policy, html.AsSpan(at));
    }

    public static bool Ours(Uri? url) =>
        url is not null && (url.AbsoluteUri == "about:blank"
        || url.IsFile && string.Equals(Path.GetDirectoryName(url.LocalPath)?.Normalize(), Folder.Normalize(), StringComparison.OrdinalIgnoreCase));

    // A navigation the view refused or a newer page cut short completes too, often while the next page loads.
    public static bool Of(Uri? request, string file) =>
        request is null || request.IsFile && string.Equals(Path.GetFullPath(request.LocalPath), file, StringComparison.OrdinalIgnoreCase);

    // A crash while a page is shown leaves its file, and with it the case's data, behind.
    public static void Clear(string dir)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.EnumerateFiles(dir, "*.html"))
        {
            try { File.Delete(file); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
    }
}

using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

public class WebPagesTests
{
    const string Doctype = "<!DOCTYPE html>";

    [Fact]
    public void ClearingRemovesThePagesACrashLeftBehind()
    {
        using var tmp = new TempDir();
        File.WriteAllText(tmp.Sub("a.html"), "<p>Zum Fass GmbH</p>");
        File.WriteAllText(tmp.Sub("notiz.txt"), "bleibt");

        WebPages.Clear(tmp.Path);
        WebPages.Clear(tmp.Sub("fehlt"));

        Assert.Equal(["notiz.txt"], Directory.EnumerateFiles(tmp.Path).Select(Path.GetFileName));
    }

    [Fact]
    public void ThePolicyForbidsScriptAndEveryRequest()
    {
        Assert.Contains("default-src 'none'", WebPages.Policy);
        Assert.DoesNotContain("script-src", WebPages.Policy);
        Assert.DoesNotContain("http", WebPages.Policy.Replace("http-equiv", ""));
        Assert.DoesNotContain("*", WebPages.Policy);
    }

    [Fact]
    public void ThePolicyComesRightBehindTheDoctype() =>
        Assert.StartsWith(Doctype + WebPages.Policy, WebPages.Seal(Doctype + "<html><body>x</body></html>"));

    [Theory]
    [InlineData("<html><body>x</body></html>")]
    [InlineData("")]
    [InlineData("<img src=\"http://example.com/a.png\"><!DOCTYPE html>")]
    [InlineData("<script src=\"http://example.com/a.js\"></script>")]
    [InlineData("{{ case.label }}")]
    public void WithoutALeadingDoctypeThePolicyComesFirst(string html) =>
        Assert.StartsWith(WebPages.Policy, WebPages.Seal(html));

    [Theory]
    [InlineData("﻿<!doctype html>")]
    [InlineData("\n  <!-- Bericht -->\n<!DOCTYPE html>")]
    [InlineData("<!DOCTYPE html PUBLIC \"-//W3C//DTD HTML 4.01//EN\">")]
    public void ALeadingDoctypeStaysAhead(string head) =>
        Assert.StartsWith(head + WebPages.Policy, WebPages.Seal(head + "<p>x</p>"));

    [Fact]
    public void APolicyOfThePageComesAfterOurs()
    {
        var sealedPage = WebPages.Seal(Doctype + "<head><meta http-equiv=\"Content-Security-Policy\" content=\"default-src *\"></head>");
        Assert.True(sealedPage.IndexOf(WebPages.Policy, StringComparison.Ordinal) < sealedPage.IndexOf("default-src *", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnclosedCommentDoesNotHideThePolicy() =>
        Assert.StartsWith(WebPages.Policy, WebPages.Seal("<!-- <!DOCTYPE html> <script src=x></script>"));

    [Fact]
    public void TheBerichtIsSealedOnce()
    {
        var rules = TestData.Seed();
        var kase = Vorlage.Load();
        var sealedPage = WebPages.Seal(Html.Render(kase, rules, Calculation.Run(kase, rules), null));

        Assert.StartsWith(Doctype + WebPages.Policy, sealedPage.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(sealedPage.IndexOf(WebPages.Policy, StringComparison.Ordinal), sealedPage.LastIndexOf(WebPages.Policy, StringComparison.Ordinal));
    }

    static Uri Page(string name) => new(Path.Combine(WebPages.Folder, name));

    [Fact]
    public void OnlyOurOwnPagesAreNavigatedTo()
    {
        Assert.True(WebPages.Ours(Page($"{Guid.NewGuid():N}.html")));
        Assert.True(WebPages.Ours(new Uri(Page("a.html") + "#anhang")));
        Assert.True(WebPages.Ours(new Uri("about:blank")));
        Assert.True(WebPages.Ours(new Uri(Page("a.html").AbsoluteUri.Normalize(System.Text.NormalizationForm.FormD))));
    }

    [Theory]
    [InlineData("http://127.0.0.1/x")]
    [InlineData("https://example.com/")]
    [InlineData("ftp://example.com/")]
    [InlineData("data:text/html,<p>x</p>")]
    [InlineData("javascript:alert(1)")]
    [InlineData("about:srcdoc")]
    [InlineData("file:///etc/passwd")]
    [InlineData("file://server/share/a.html")]
    public void EverythingElseIsNot(string url) => Assert.False(WebPages.Ours(new Uri(url)));

    [Fact]
    public void NeitherIsANeighbourOfOurPages()
    {
        Assert.False(WebPages.Ours(null));
        Assert.False(WebPages.Ours(Page("../store/rules.db")));
        Assert.False(WebPages.Ours(Page("sub/a.html")));
        Assert.False(WebPages.Ours(new Uri(Path.Combine(AppData.Dir, "pages-evil", "a.html"))));
    }
}

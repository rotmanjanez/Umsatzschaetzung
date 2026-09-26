using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Umsatzschaetzung.App.Platform;

namespace Umsatzschaetzung.Tests.Ui;

// Careless templates in the real web view: whatever they ask for, nothing reaches the sink.
public class SandboxTests
{
    public sealed record Seen(bool Loaded, string[] Requests, string[] Navigations, string? Failure);

    static readonly (string Name, string Html)[] Mistakes =
    [
        ("img", "<img src=\"{0}/img\">"),
        ("srcset", "<img srcset=\"{0}/srcset 2x\"><picture><source srcset=\"{0}/source\"><img></picture>"),
        ("stylesheet", "<link rel=\"stylesheet\" href=\"{0}/stylesheet\">"),
        ("import", "<style>@import url(\"{0}/import\");</style>"),
        ("background", "<style>body {{ background: url(\"{0}/background\") }}</style>"),
        ("font", "<style>@font-face {{ font-family: F; src: url(\"{0}/font\") }} body {{ font-family: F }}</style>x"),
        ("attribute", "<body background=\"{0}/attribute\"><table background=\"{0}/table\"><tr><td>x</td></tr></table></body>"),
        ("script", "<script src=\"{0}/script\"></script>"),
        ("inline", "<script>fetch(\"{0}/inline\"); new Image().src = \"{0}/inline-image\";</script>"),
        ("handler", "<img src=\"missing.png\" onerror=\"new Image().src='{0}/handler'\">"),
        ("svg", "<svg><image href=\"{0}/svg\" width=\"10\" height=\"10\"/></svg><svg onload=\"fetch('{0}/svg-onload')\"></svg>"),
        ("iframe", "<iframe src=\"{0}/iframe\"></iframe><iframe srcdoc=\"<img src='{0}/srcdoc'>\"></iframe>"),
        ("object", "<object data=\"{0}/object\"></object><embed src=\"{0}/embed\">"),
        ("media", "<video src=\"{0}/video\" poster=\"{0}/poster\" autoplay></video><audio src=\"{0}/audio\" autoplay></audio>"),
        ("input", "<input type=\"image\" src=\"{0}/input\">"),
        ("preload", "<link rel=\"preload\" as=\"image\" href=\"{0}/preload\"><link rel=\"prefetch\" href=\"{0}/prefetch\"><link rel=\"preconnect\" href=\"{0}\">"),
        ("icon", "<link rel=\"icon\" href=\"{0}/icon\">"),
        ("base", "<base href=\"{0}/base/\"><img src=\"relative.png\">"),
        ("refresh", "<meta http-equiv=\"refresh\" content=\"0; url={0}/refresh\">"),
        ("form", "<form action=\"{0}/form\" method=\"post\"><input name=\"q\" value=\"x\" autofocus onfocus=\"this.form.submit()\"></form>"),
        ("javascript-url", "<iframe src=\"javascript:fetch('{0}/javascript-url')\"></iframe>"),
        ("looser-policy", "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src * 'unsafe-inline'\"><img src=\"{0}/looser-policy\">"),
        ("page-in-head", "<!DOCTYPE html><html><head><link rel=\"stylesheet\" href=\"{0}/page-in-head\"></head><body>x</body></html>"),
        ("comment-first", "<!-- <!DOCTYPE html> --><img src=\"{0}/comment-first\">"),
    ];

    [Fact]
    public void CarelessTemplatesRequestNothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("the web view on macOS only runs on the main thread");
            return;
        }
        var seen = Desktop.Run(Probe);

        Assert.Null(seen.Failure);
        Assert.True(seen.Loaded);
        Assert.Equal(["/control"], seen.Requests);
        Assert.DoesNotContain(seen.Navigations, n => n.EndsWith("/ran", StringComparison.Ordinal));
    }

    // The control page skips the policy: its request proves the sink is reachable at all, so the
    // silence of every sealed page means something.
    public static Seen Probe()
    {
        using var sink = new Sink();
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var view = new HtmlView();
        var window = new Window { Width = 800, Height = 600, Content = view };
        ConcurrentQueue<string> navigations = [];
        view.NavigationStarted += (_, e) => navigations.Enqueue(e.Request?.ToString() ?? "");
        var loaded = true;
        string? failure = null;
        window.Opened += (_, _) => Dispatcher.UIThread.Post(async () =>
        {
            try
            {
                loaded &= await Raw(view, $"<img src=\"{sink.Url}/control\">", done.Token);
                loaded &= await Raw(view, $"<script>location.href = \"{sink.Url}/ran\";</script>", done.Token);
                view.Navigate(new Uri(sink.Url + "/navigate"));
                foreach (var (name, html) in Mistakes)
                    if (!await view.Show(string.Format(html, sink.Url), TimeSpan.FromSeconds(20), done.Token))
                        failure = (failure is null ? "" : failure + ", ") + name + " did not load";
                await Task.Delay(1500, done.Token);
            }
            catch (Exception e)
            {
                failure = e.ToString();
            }
            finally
            {
                done.Cancel();
            }
        });
        window.Show();
        try
        {
            Dispatcher.UIThread.MainLoop(done.Token);
        }
        finally
        {
            window.Close();
        }
        return new Seen(loaded, [.. sink.Requests.Order()], [.. navigations], failure);
    }

    static async Task<bool> Raw(HtmlView view, string html, CancellationToken ct)
    {
        Directory.CreateDirectory(WebPages.Folder);
        var file = Path.Combine(WebPages.Folder, $"{Guid.NewGuid():N}.html");
        await File.WriteAllTextAsync(file, html, Encoding.UTF8, ct);
        var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Completed(object? sender, WebViewNavigationCompletedEventArgs e)
        {
            if (WebPages.Of(e.Request, file)) loaded.TrySetResult(e.IsSuccess);
        }
        view.NavigationCompleted += Completed;
        try
        {
            view.Navigate(new Uri(file));
            var ok = await loaded.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
            await Task.Delay(500, ct);
            return ok;
        }
        finally
        {
            view.NavigationCompleted -= Completed;
            File.Delete(file);
        }
    }

    sealed class Sink : IDisposable
    {
        readonly TcpListener listener = new(IPAddress.Loopback, 0);
        public readonly ConcurrentBag<string> Requests = [];
        public string Url { get; }

        public Sink()
        {
            listener.Start();
            Url = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
            _ = Accept();
        }

        async Task Accept()
        {
            try
            {
                while (true)
                    _ = Answer(await listener.AcceptTcpClientAsync());
            }
            catch (ObjectDisposedException) { }
            catch (SocketException) { }
        }

        async Task Answer(TcpClient client)
        {
            using (client)
            {
                var stream = client.GetStream();
                var line = await new StreamReader(stream, Encoding.ASCII).ReadLineAsync() ?? "";
                var parts = line.Split(' ');
                Requests.Add(parts.Length > 1 ? parts[1] : line);
                await stream.WriteAsync("HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n"u8.ToArray());
            }
        }

        public void Dispose() => listener.Stop();
    }
}

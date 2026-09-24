using SkiaSharp;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Scan;

public class PdfiumPagesTests
{
    static readonly byte[] Zugferd = File.ReadAllBytes(TestData.File("zugferd.pdf"));

    readonly PdfiumPages pdf = new();

    static async Task<List<(int W, int H)>> Sizes(IAsyncEnumerable<SKBitmap> pages)
    {
        var sizes = new List<(int, int)>();
        await foreach (var page in pages)
            using (page)
                sizes.Add((page.Width, page.Height));
        return sizes;
    }

    [Theory]
    [InlineData(72, 595, 842)]
    [InlineData(150, 1240, 1754)]
    [InlineData(300, 2480, 3508)]
    public async Task EachPageRendersAtTheAskedResolution(int dpi, int w, int h)
    {
        Assert.Equal([(w, h)], await Sizes(pdf.Rasterize(Zugferd, dpi, TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task APageIsRenderedOnWhitePaperWithItsTextInInk()
    {
        await foreach (var page in pdf.Rasterize(Zugferd, 72, TestContext.Current.CancellationToken))
            using (page)
            {
                Assert.Equal(SKColors.White, page.GetPixel(2, 2));
                Assert.InRange(Sheets.Dark(page), 500, page.Width * page.Height / 4);
            }
    }

    [Fact]
    public async Task APageRendersOnItsOwn()
    {
        using var page = await pdf.Page(Zugferd, 0, 72, TestContext.Current.CancellationToken);
        Assert.Equal((595, 842), (page.Width, page.Height));
        await Assert.ThrowsAsync<InvalidDataException>(() => pdf.Page(Zugferd, 1, 72, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoppingEarlyReleasesTheDocument()
    {
        var ct = TestContext.Current.CancellationToken;
        for (var i = 0; i < 3; i++)
            await foreach (var page in pdf.Rasterize(Zugferd, 72, ct))
            {
                page.Dispose();
                break;
            }
        Assert.Single(await Sizes(pdf.Rasterize(Zugferd, 72, ct)));
    }

    [Fact]
    public async Task ACancelledRenderStopsBeforeTheFirstPage()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Sizes(pdf.Rasterize(Zugferd, 72, cts.Token)));
    }

    [Fact]
    public async Task AnUnreadablePdfIsInvalidData()
    {
        var broken = "%PDF-1.7\nnot a pdf"u8.ToArray();
        await Assert.ThrowsAsync<InvalidDataException>(() => Sizes(pdf.Rasterize(broken, 72, TestContext.Current.CancellationToken)));
    }
}

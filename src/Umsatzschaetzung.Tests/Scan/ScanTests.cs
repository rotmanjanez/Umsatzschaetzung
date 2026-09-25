using System.Runtime.CompilerServices;
using SkiaSharp;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Reader = Umsatzschaetzung.Service.Scan;

namespace Umsatzschaetzung.Tests.Scan;

public class ScanTests
{
    static readonly byte[] Png = Encode();
    static readonly byte[] Pdf = "%PDF-1.7\n"u8.ToArray();
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    static byte[] Encode()
    {
        using var page = Sheets.Blank(20, 30);
        return Sheets.Png(page);
    }

    sealed class Ocr(Action<int>? onPage = null) : IOcr
    {
        public List<byte[]> Images { get; } = [];
        public List<SKBitmap> Pages { get; } = [];
        public List<bool> Alive { get; } = [];
        public List<CancellationToken> Tokens { get; } = [];

        public Task<OcrPage> Recognize(byte[] image, CancellationToken ct)
        {
            Images.Add(image);
            Tokens.Add(ct);
            return Task.FromResult(new OcrPage());
        }

        public Task<OcrPage> Recognize(SKBitmap page, CancellationToken ct)
        {
            Alive.AddRange(Pages.Select(p => p.Handle != IntPtr.Zero));
            Pages.Add(page);
            Tokens.Add(ct);
            onPage?.Invoke(Pages.Count);
            return Task.FromResult(new OcrPage { Width = page.Width, Height = page.Height });
        }

        public Task<List<OcrWord>> Read(Raster crop, CancellationToken ct) => Task.FromResult<List<OcrWord>>([]);
    }

    sealed class Held : IOcr
    {
        public TaskCompletionSource<OcrPage> First { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Started { get; private set; }

        public Task<OcrPage> Recognize(byte[] image, CancellationToken ct) => throw new NotSupportedException();

        public Task<OcrPage> Recognize(SKBitmap page, CancellationToken ct) =>
            ++Started == 1 ? First.Task : Task.FromResult(new OcrPage { Width = page.Width });

        public Task<List<OcrWord>> Read(Raster crop, CancellationToken ct) => Task.FromResult<List<OcrWord>>([]);
    }

    sealed class Pages(int count) : IPdfPages
    {
        public int Produced { get; private set; }
        public List<int> ProducedAtRecognize { get; } = [];
        public CancellationToken Token { get; private set; }
        public int Dpi { get; private set; }

        public Task<SKBitmap> Page(byte[] pdf, int index, int dpi, CancellationToken ct) => Task.FromResult(Sheets.Blank(10 + index, 10));

        public async IAsyncEnumerable<SKBitmap> Rasterize(byte[] pdf, int dpi, [EnumeratorCancellation] CancellationToken ct)
        {
            Token = ct;
            Dpi = dpi;
            for (var i = 0; i < count; i++)
            {
                ct.ThrowIfCancellationRequested();
                await Task.Yield();
                Produced++;
                yield return Sheets.Blank(10 + i, 10);
            }
        }
    }

    [Fact]
    public async Task AnImageGoesStraightToTheRecogniser()
    {
        var ocr = new Ocr();
        var pages = await Reader.Read(ocr, null, "scan.png", Png, Reader.Dpi, Ct);
        Assert.Same(Png, Assert.Single(ocr.Images));
        Assert.Empty(ocr.Pages);
    }

    [Fact]
    public async Task PdfPagesAreRasterizedOneByOneAndReleasedAfterReading()
    {
        var pdf = new Pages(3);
        var produced = new List<int>();
        var reader = new Ocr(_ => produced.Add(pdf.Produced));
        var pages = await Reader.Read(reader, pdf, "scan.pdf", Pdf, 200, Ct);
        Assert.Equal([10, 11, 12], pages.Select(p => p.Width));
        Assert.Equal([1, 2, 3], produced);
        Assert.Equal(200, pdf.Dpi);
        Assert.All(reader.Pages, p => Assert.Equal(IntPtr.Zero, p.Handle));
        Assert.DoesNotContain(true, reader.Alive);
    }

    [Fact]
    public async Task TheNextPageIsReadWhileTheOneBeforeIsStillBeingRead()
    {
        var ocr = new Held();
        var reading = Reader.Read(ocr, new Pages(2), "scan.pdf", Pdf, Reader.Dpi, Ct);
        Assert.True(SpinWait.SpinUntil(() => ocr.Started == 2, TimeSpan.FromSeconds(5)));
        ocr.First.SetResult(new OcrPage { Width = 10 });
        Assert.Equal([10, 11], (await reading).Select(p => p.Width));
    }

    [Fact]
    public async Task AZugferdPdfIsReadAsAScanToo()
    {
        var ocr = new Ocr();
        var pages = await Reader.Read(ocr, new PdfiumPages(), "zugferd.pdf", File.ReadAllBytes(TestData.File("zugferd.pdf")), 72, Ct);
        Assert.Equal((595, 842), (Assert.Single(pages).Width, pages[0].Height));
    }

    [Fact]
    public async Task WithoutARecogniserNothingIsUnsupported()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => Reader.Read(null, new Pages(1), "scan.png", Png, Reader.Dpi, Ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }

    [Fact]
    public async Task APdfWithoutARendererIsUnsupported()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => Reader.Read(new Ocr(), null, "scan.pdf", Pdf, Reader.Dpi, Ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }

    [Theory]
    [InlineData("hello")]
    [InlineData("<?xml version=\"1.0\"?><Invoice xmlns=\"urn:oasis:names:specification:ubl:schema:xsd:Invoice-2\"/>")]
    [InlineData("")]
    public async Task AnythingButAnImageOrPdfIsNoScan(string content)
    {
        var ocr = new Ocr();
        var e = await Assert.ThrowsAsync<ServiceError>(() =>
            Reader.Read(ocr, new Pages(1), "x.txt", System.Text.Encoding.UTF8.GetBytes(content), Reader.Dpi, Ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
        Assert.Contains("x.txt", e.Message);
        Assert.Empty(ocr.Tokens);
    }

    [Fact]
    public async Task APdfWithoutPagesIsUnsupported()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => Reader.Read(new Ocr(), new Pages(0), "leer.pdf", Pdf, Reader.Dpi, Ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
        Assert.Contains("leer.pdf", e.Message);
    }

    [Fact]
    public async Task TheTokenReachesTheRendererAndTheRecogniser()
    {
        using var cts = new CancellationTokenSource();
        var ocr = new Ocr();
        var pdf = new Pages(2);
        await Reader.Read(ocr, pdf, "scan.pdf", Pdf, Reader.Dpi, cts.Token);
        Assert.Equal(cts.Token, pdf.Token);
        Assert.All(ocr.Tokens, t => Assert.Equal(cts.Token, t));
    }

    [Fact]
    public async Task CancellingMidDocumentStopsBeforeTheNextPage()
    {
        using var cts = new CancellationTokenSource();
        var ocr = new Ocr(_ => cts.Cancel());
        var pdf = new Pages(3);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Reader.Read(ocr, pdf, "scan.pdf", Pdf, Reader.Dpi, cts.Token));
        Assert.Equal(1, pdf.Produced);
        Assert.Equal(IntPtr.Zero, Assert.Single(ocr.Pages).Handle);
    }

    [Fact]
    public async Task CancellingARealRenderStopsIt()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Reader.Read(new Ocr(), new PdfiumPages(), "zugferd.pdf", File.ReadAllBytes(TestData.File("zugferd.pdf")), 72, cts.Token));
    }

    [Fact]
    public async Task APageOfAPdfIsAskedOfTheRenderer()
    {
        using var page = await Reader.Page(new Pages(2), Pdf, 1, 100, Ct);
        Assert.Equal(11, page.Width);
    }

    [Fact]
    public async Task AnImageIsItsOnlyPage()
    {
        using var page = await Reader.Page(null, Png, 0, 100, Ct);
        Assert.Equal((20, 30), (page.Width, page.Height));
        await Assert.ThrowsAsync<InvalidDataException>(() => Reader.Page(null, Png, 1, 100, Ct));
    }

    [Fact]
    public async Task APageWithoutARendererIsUnsupported()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => Reader.Page(null, Pdf, 0, 100, Ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }
}

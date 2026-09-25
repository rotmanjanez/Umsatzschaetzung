using SkiaSharp;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class ReadingsTests : IDisposable
{
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    readonly TempDir dir = new();
    readonly Ocr ocr = new();
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public void Dispose() => dir.Dispose();

    sealed class Ocr : IOcr
    {
        public int Calls { get; private set; }

        public Task<OcrPage> Recognize(byte[] image, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new OcrPage
            {
                Width = 2480,
                Height = 3508,
                Correction = new Correction { Skew = 0.4 },
                Words = [new OcrWord { Text = "Rechnung", Box = new Box(200, 300, 400, 60), Confidence = 0.98f }],
            });
        }

        public Task<OcrPage> Recognize(SKBitmap page, CancellationToken ct) => throw new NotSupportedException();

        public Task<List<OcrWord>> Read(Raster crop, CancellationToken ct) => Task.FromResult<List<OcrWord>>([]);
    }

    [Fact]
    public async Task TheSameDocumentIsReadOnceAndReplayedAsReadAcrossServices()
    {
        using var first = new Host(ocr, readings: new Readings(dir.Path, "test"));
        using var second = new Host(ocr, readings: new Readings(dir.Path, "test"));

        var read = await first.Service.OcrInvoice("", "scan.png", Png, ct);
        var replayed = await second.Service.OcrInvoice("", "kopie.png", Png, ct);

        Assert.Equal(1, ocr.Calls);
        Assert.NotEqual(read.InvoiceId, replayed.InvoiceId);
        Assert.Equal("kopie.png", replayed.Draft.FileName);
        Assert.Equal(Json.Serialize(read.Pages[0].Words), Json.Serialize(replayed.Pages[0].Words));
        Assert.Equal(0.4, replayed.Pages[0].Correction.Skew);
    }

    [Fact]
    public async Task AnotherDocumentIsRead()
    {
        using var host = new Host(ocr, readings: new Readings(dir.Path, "test"));

        await host.Service.OcrInvoice("", "a.png", Png, ct);
        await host.Service.OcrInvoice("", "b.png", [.. Png, 4], ct);

        Assert.Equal(2, ocr.Calls);
    }
}

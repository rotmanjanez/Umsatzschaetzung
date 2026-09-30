using SkiaSharp;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Service;
using Reader = Umsatzschaetzung.Service.Scan;

namespace Umsatzschaetzung.Tests.Scan;

public sealed class OcrFixture : IDisposable
{
    public RapidOcr Ocr { get; } = new(new OrtWeights(AppFiles.Beside("models")));

    public void Dispose() => Ocr.Dispose();
}

public class RapidOcrTests(OcrFixture fixture) : IClassFixture<OcrFixture>
{
    static CancellationToken Ct => TestContext.Current.CancellationToken;

    async Task<OcrPage> Read(byte[] image) => Assert.Single(await Reader.Read(fixture.Ocr, null, "scan.png", image, Reader.Dpi, 0, Ct));

    // 12 pt at 300 dpi.
    static SKBitmap Line(string text, int width = 1000, int height = 240)
    {
        var page = Sheets.Blank(width, height);
        using var canvas = new SKCanvas(page);
        using var font = new SKFont(SKTypeface.Default, 50);
        using var ink = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        canvas.DrawText(text, 150, 140, font, ink);
        return page;
    }

    static void Plausible(OcrPage page, int width, int height)
    {
        Assert.Equal((width, height), (page.Width, page.Height));
        var rechnung = Assert.Single(page.Words, w => w.Text == "Rechnung");
        var number = Assert.Single(page.Words, w => w.Text == "4711");
        foreach (var box in new[] { rechnung.Box, number.Box })
        {
            Assert.InRange(box.X, 100, width);
            Assert.InRange(box.X + box.W, 0, width);
            Assert.InRange(box.Y, 60, 140);
            Assert.InRange(box.H, 25, 125);
        }
        Assert.InRange(rechnung.Box.W, 150, 330);
        Assert.True(rechnung.Box.X + rechnung.Box.W <= number.Box.X);
        Assert.InRange(Math.Abs(rechnung.Box.Y - number.Box.Y), 0, 15);
        Assert.All(page.Words, w => Assert.InRange(w.Confidence, 0.5f, 1f));
    }

    [Fact]
    public async Task ARenderedLineIsReadWordByWordWithItsBoxes()
    {
        using var line = Line("Rechnung 4711");
        Plausible(await fixture.Ocr.Recognize(line, Ct), line.Width, line.Height);
    }

    [Fact]
    public async Task AnEncodedImageIsReadTheSame()
    {
        using var line = Line("Rechnung 4711");
        Plausible(await Read(Sheets.Png(line)), line.Width, line.Height);
    }

    [Fact]
    public async Task AnUpsideDownPageIsTurnedAndARenderOfItReplaysTheTurn()
    {
        using var line = Line("Rechnung 4711");
        using var flipped = new SKBitmap(line.Info);
        using (var canvas = new SKCanvas(flipped))
        {
            canvas.RotateDegrees(180, line.Width / 2f, line.Height / 2f);
            canvas.DrawBitmap(line, 0, 0);
        }
        var png = Sheets.Png(flipped);

        var page = await Read(png);

        Assert.Equal(180, page.Correction.Turn);
        Plausible(page, line.Width, line.Height);
        var shown = Reader.Upright(flipped, page.Correction);
        Assert.Equal((page.Width, page.Height), (shown.Width, shown.Height));
    }

    [Fact]
    public void AnUncorrectedRenderKeepsItsPixels()
    {
        using var ruled = Sheets.Ruled(width: 40, height: 30);
        using var shown = Sheets.Of(Reader.Upright(ruled, new Correction()));
        Assert.Equal(ruled.Bytes, shown.Bytes);
    }

    [Fact]
    public async Task ABlankPageHasNoWordsAndNoImage()
    {
        using var blank = Sheets.Blank(400, 300);
        var page = await Read(Sheets.Png(blank));
        Assert.Empty(page.Words);
        Assert.Null(page.Image);
        Assert.Equal((400, 300), (page.Width, page.Height));
    }
}

using SkiaSharp;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Reader = Umsatzschaetzung.Service.Scan;

namespace Umsatzschaetzung.Tests.Scan;

public class UprightTests
{
    public static TheoryData<double, double, int, double> Corrections => new()
    {
        { 1, 0, 180, 0 },
        { 1, 2.5, 0, 0 },
        { 0.5, -3, 90, 0 },
        { 0.75, 1.5, 270, -0.5 },
        { 1, 0, 90, 2 },
    };

    // The reader's steps one after the other, as the recogniser takes them.
    static SKBitmap Stepwise(SKBitmap page, Correction c)
    {
        var at = page.Copy();
        if (c.Scale != 1)
            at = Next(at, at.Resize(new SKImageInfo((int)Math.Round(at.Width * c.Scale), (int)Math.Round(at.Height * c.Scale), SKColorType.Bgra8888, SKAlphaType.Premul),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear)));
        if (c.Skew != 0) at = Next(at, Deskew.Straighten(at, c.Skew));
        if (c.Turn != 0) at = Next(at, RapidOcr.Rotate(at, c.Turn));
        if (c.Settle != 0) at = Next(at, Deskew.Straighten(at, c.Settle));
        return at;
    }

    static SKBitmap Next(SKBitmap was, SKBitmap next)
    {
        was.Dispose();
        return next;
    }

    static (double X, double Y) Centre(SKBitmap page)
    {
        double x = 0, y = 0, n = 0;
        for (var j = 0; j < page.Height; j++)
            for (var i = 0; i < page.Width; i++)
                if (page.GetPixel(i, j).Red < 128) { x += i; y += j; n++; }
        return (x / n, y / n);
    }

    static SKBitmap Marked()
    {
        var page = Sheets.Ruled(width: 300, height: 400);
        using var canvas = new SKCanvas(page);
        using var ink = new SKPaint { Color = SKColors.Black };
        canvas.DrawRect(20, 20, 60, 30, ink);
        return page;
    }

    [Theory]
    [MemberData(nameof(Corrections))]
    public void OnePassLandsWhereTheStepsDo(double scale, double skew, int turn, double settle)
    {
        var c = new Correction { Scale = scale, Skew = skew, Turn = turn, Settle = settle };
        using var page = Marked();
        using var steps = Stepwise(page, c);

        using var once = Sheets.Of(Reader.Upright(page, c));

        Assert.Equal((steps.Width, steps.Height), (once.Width, once.Height));
        var (a, b) = (Centre(steps), Centre(once));
        Assert.InRange(Math.Abs(a.X - b.X), 0, 0.5);
        Assert.InRange(Math.Abs(a.Y - b.Y), 0, 0.5);
    }

    // Resampled at other offsets, a filtered pixel may round a level apart.
    [Theory]
    [MemberData(nameof(Corrections))]
    public void ACutIsThatRegionOfTheWholePage(double scale, double skew, int turn, double settle)
    {
        var c = new Correction { Scale = scale, Skew = skew, Turn = turn, Settle = settle };
        using var page = Marked();
        using var whole = Sheets.Of(Reader.Upright(page, c));
        var region = new SKRectI(10, 30, whole.Width - 20, 70);

        using var cut = Sheets.Of(Reader.Cut(page, c, region)!);
        using var expected = new SKBitmap();
        whole.ExtractSubset(expected, region);

        Assert.Equal((region.Width, region.Height), (cut.Width, cut.Height));
        for (var y = 0; y < cut.Height; y++)
            for (var x = 0; x < cut.Width; x++)
                Assert.InRange(Math.Abs(expected.GetPixel(x, y).Red - cut.GetPixel(x, y).Red), 0, 2);
    }

    [Fact]
    public void ACutIsKeptToThePage()
    {
        using var page = Sheets.Blank(40, 30);
        var cut = Reader.Cut(page, new Correction(), new SKRectI(-10, 20, 50, 40))!;
        Assert.Equal((40, 10), (cut.Width, cut.Height));
        Assert.Null(Reader.Cut(page, new Correction(), new SKRectI(50, 0, 60, 10)));
    }

    [Fact]
    public void AGreyScanIsDrawnInTheLayoutTheViewTakes()
    {
        using var grey = new SKBitmap(new SKImageInfo(4, 2, SKColorType.Gray8, SKAlphaType.Opaque));
        grey.Erase(new SKColor(0x40, 0x40, 0x40));
        var shown = Reader.Upright(grey, new Correction());
        Assert.Equal(4 * 2 * 4, shown.Pixels.Length);
        Assert.Equal(new byte[] { 0x40, 0x40, 0x40, 0xFF }, shown.Pixels[..4]);
    }
}

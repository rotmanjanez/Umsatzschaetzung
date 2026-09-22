using SkiaSharp;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Scan;

public class DeinkTests
{
    [Theory]
    [InlineData(205)]
    [InlineData(180)]
    public void ShowThroughIsRemovedAndTheFrontSurvivesWhole(byte ghost)
    {
        using var sheet = Sheets.Doubled(ghost);
        using var cleaned = Deink.Apply(sheet);
        Assert.NotNull(cleaned);
        var before = Sheets.Ink(sheet);
        var after = Sheets.Ink(cleaned);
        Assert.True(before.Right > 0);
        Assert.Equal(0, after.Right);
        Assert.True(after.Left >= before.Left * 0.95, $"{before.Left} -> {after.Left}");
    }

    [Fact]
    public void TheFrontKeepsItsTone()
    {
        using var sheet = Sheets.Doubled();
        using var cleaned = Deink.Apply(sheet)!;
        Assert.Equal((sheet.Width, sheet.Height), (cleaned.Width, cleaned.Height));
        Assert.InRange(cleaned.GetPixel(50, 64).Red, 0, 40);
        Assert.Equal(SKColors.White, cleaned.GetPixel(340, 73));
    }

    [Fact]
    public void ASingleSidedPageKeepsItsInk()
    {
        using var flat = Sheets.Ruled();
        using var cleaned = Deink.Apply(flat);
        var before = Sheets.Dark(flat);
        Assert.True((cleaned is null ? before : Sheets.Dark(cleaned)) >= before * 0.95);
    }

    [Fact]
    public void GreyPrintOnTheFrontIsNotTakenForShowThrough()
    {
        using var page = Sheets.Ruled();
        using (var canvas = new SKCanvas(page))
        using (var grey = new SKPaint { Color = new SKColor(120, 120, 120) })
            canvas.DrawRect(60, 20, 200, 30, grey);
        using var cleaned = Deink.Apply(page)!;
        Assert.InRange(cleaned.GetPixel(150, 35).Red, 100, 140);
    }

    [Theory]
    [InlineData(255)]
    [InlineData(248)]
    public void AnEmptySheetHasNothingToClean(byte level)
    {
        using var page = Sheets.Blank(level: level);
        Assert.Null(Deink.Apply(page));
    }

    [Fact]
    public void ANearWhitePageWithFaintSpecklesLosesNoFrontInk()
    {
        using var page = Sheets.Blank(level: 250);
        var random = new Random(7);
        for (var i = 0; i < 2000; i++)
        {
            var v = (byte)random.Next(240, 256);
            page.SetPixel(random.Next(page.Width), random.Next(page.Height), new SKColor(v, v, v));
        }
        using (var canvas = new SKCanvas(page))
        using (var ink = new SKPaint { Color = SKColors.Black })
            canvas.DrawRect(100, 100, 200, 10, ink);
        using var cleaned = Deink.Apply(page);
        Assert.NotNull(cleaned);
        Assert.InRange(cleaned.GetPixel(200, 105).Red, 0, 20);
    }

    [Fact]
    public void APageBeyondTheRecognisersLimitComesBackAtIt()
    {
        using var page = Sheets.Ruled(width: 5000, height: 1000);
        using var cleaned = Deink.Apply(page)!;
        Assert.Equal((RapidOcr.MaxImageDimension, 800), (cleaned.Width, cleaned.Height));
    }
}

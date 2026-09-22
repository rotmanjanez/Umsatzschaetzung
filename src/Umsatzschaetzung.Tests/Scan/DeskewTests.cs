using SkiaSharp;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Scan;

public class DeskewTests
{
    [Fact]
    public void AStraightPageReadsAsStraightAndIsNotResampled()
    {
        using var flat = Sheets.Ruled();
        Assert.True(Math.Abs(Deskew.Angle(flat)) < 0.05);
        Assert.Null(Deskew.Apply(flat));
    }

    [Theory]
    [InlineData(-5.5)]
    [InlineData(-3.0)]
    [InlineData(-0.8)]
    [InlineData(0.8)]
    [InlineData(3.0)]
    [InlineData(5.5)]
    public void ALeanIsMeasuredAsTheTurnThatUndoesIt(double lean)
    {
        using var page = Sheets.Ruled(lean);
        Assert.InRange(Deskew.Angle(page), -lean - 0.1, -lean + 0.1);
    }

    [Theory]
    [InlineData(-3.0)]
    [InlineData(-0.8)]
    [InlineData(0.8)]
    [InlineData(3.0)]
    public void ALeanPutOnByStraightenIsRecovered(double lean)
    {
        using var flat = Sheets.Ruled();
        using var page = Deskew.Straighten(flat, lean);
        Assert.InRange(Deskew.Angle(page), -lean - 0.1, -lean + 0.1);
    }

    [Theory]
    [InlineData(-4.0)]
    [InlineData(2.0)]
    public void ALeaningPageIsStraightenedWithoutLosingInk(double lean)
    {
        using var leaning = Sheets.Ruled(lean);
        using var straight = Deskew.Apply(leaning);
        Assert.NotNull(straight);
        Assert.True(Math.Abs(Deskew.Angle(straight)) < 0.05);
        Assert.True(straight.Width >= leaning.Width && straight.Height >= leaning.Height);
        var before = Sheets.Dark(leaning);
        Assert.InRange(Sheets.Dark(straight), before * 0.9, before * 1.2);
    }

    [Theory]
    [InlineData(-10.0)]
    [InlineData(10.0)]
    [InlineData(-6.0)]
    [InlineData(6.0)]
    public void ALeanBeyondTheLimitIsCorrectedOnlyUpToTheLimitAndInTheRightDirection(double lean)
    {
        using var page = Sheets.Ruled(lean);
        var angle = Deskew.Angle(page);
        Assert.InRange(Math.Abs(angle), 5.0, 6.0);
        Assert.Equal(-Math.Sign(lean), Math.Sign(angle));
    }

    [Fact]
    public void StraightenGrowsTheCanvasToHoldTheWholeTurnedPageOnWhite()
    {
        using var page = Sheets.Blank(400, 200, 0);
        using var turned = Deskew.Straighten(page, 30);
        var r = Math.PI / 6;
        Assert.Equal((int)Math.Ceiling(400 * Math.Cos(r) + 200 * Math.Sin(r)), turned.Width);
        Assert.Equal((int)Math.Ceiling(400 * Math.Sin(r) + 200 * Math.Cos(r)), turned.Height);
        Assert.Equal(SKColors.White, turned.GetPixel(0, 0));
        Assert.Equal(SKColors.Black, turned.GetPixel(turned.Width / 2, turned.Height / 2));
    }

    [Fact]
    public void StraightenByZeroKeepsThePage()
    {
        using var page = Sheets.Ruled();
        using var same = Deskew.Straighten(page, 0);
        Assert.Equal((page.Width, page.Height), (same.Width, same.Height));
        Assert.Equal(Sheets.Dark(page), Sheets.Dark(same));
    }

    [Fact]
    public void ABlankPageHasNoLeanAndIsNotResampled()
    {
        using var blank = Sheets.Blank();
        Assert.Equal(0, Deskew.Angle(blank));
        Assert.Null(Deskew.Apply(blank));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(5, 5)]
    [InlineData(10, 400)]
    public void AnImageTooSmallToMeasureIsLeftAlone(int width, int height)
    {
        using var tiny = Sheets.Blank(width, height, 0);
        Assert.Equal(0, Deskew.Angle(tiny));
        Assert.Null(Deskew.Apply(tiny));
    }

    [Fact]
    public void ABorderAroundTheSheetDoesNotOutvoteTheText()
    {
        using var page = Sheets.Ruled(3);
        using (var canvas = new SKCanvas(page))
        using (var frame = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 12 })
            canvas.DrawRect(6, 6, page.Width - 12, page.Height - 12, frame);
        Assert.InRange(Deskew.Angle(page), -3.1, -2.9);
    }
}

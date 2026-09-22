using SkiaSharp;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Scan;

public class InkTests
{
    [Theory]
    [InlineData(255, 255, 255, 255)]
    [InlineData(0, 0, 0, 0)]
    [InlineData(255, 0, 0, 76)]
    [InlineData(0, 255, 0, 150)]
    [InlineData(0, 0, 255, 27)]
    public void GreyWeighsTheChannelsLikeTheEye(byte r, byte g, byte b, byte expected)
    {
        using var page = new SKBitmap(new SKImageInfo(4, 4, SKColorType.Bgra8888, SKAlphaType.Premul));
        page.Erase(new SKColor(r, g, b));
        Assert.All(Ink.Grey(page, 4, 4), v => Assert.Equal(expected, v));
    }

    [Fact]
    public void GreyScalesToTheAskedSize()
    {
        using var page = Sheets.Blank(100, 50, 128);
        var grey = Ink.Grey(page, 10, 5);
        Assert.Equal(50, grey.Length);
        Assert.All(grey, v => Assert.InRange(v, 127, 129));
    }

    [Fact]
    public void DepthIsNearZeroOnUnevenlyLitPaper()
    {
        var grey = Enumerable.Range(0, 64 * 64).Select(i => (byte)(120 + i % 64 * 2)).ToArray();
        Assert.All(Ink.Depth(grey, 64, 64), v => Assert.InRange(v, 0, 8));
    }

    [Fact]
    public void DepthMeasuresInkBelowTheLocalPaper()
    {
        const int w = 64, h = 64;
        var grey = Enumerable.Repeat((byte)200, w * h).ToArray();
        grey[32 * w + 32] = 50;
        var depth = Ink.Depth(grey, w, h);
        Assert.Equal(150, depth[32 * w + 32]);
        Assert.Equal(0, depth[0]);
    }

    [Fact]
    public void DepthCopesWithAnImageSmallerThanTheGrid()
    {
        Assert.Equal(3, Ink.Depth([255, 0, 255], 3, 1).Length);
    }

    [Fact]
    public void OtsuSplitsTwoLevels()
    {
        byte[] values = [.. Enumerable.Repeat((byte)10, 100), .. Enumerable.Repeat((byte)200, 30)];
        Assert.InRange(Ink.Otsu(values), 10, 199);
    }

    [Fact]
    public void OtsuOnlyWeighsValuesAboveTheGivenLevel()
    {
        byte[] values = [.. Enumerable.Repeat((byte)0, 1000), .. Enumerable.Repeat((byte)60, 50), .. Enumerable.Repeat((byte)200, 50)];
        Assert.InRange(Ink.Otsu(values, 0), 60, 199);
    }

    [Fact]
    public void OtsuOfNothingOrOneLevelIsZero()
    {
        Assert.Equal(0, Ink.Otsu([]));
        Assert.Equal(0, Ink.Otsu([7, 7, 7]));
        Assert.Equal(0, Ink.Otsu([7, 7, 7], 7));
    }

    [Fact]
    public void MedianCountsOnlyValuesAboveTheLevel()
    {
        Assert.Equal(30, Ink.Median([0, 0, 0, 0, 10, 30, 50], 0));
        Assert.Equal(255, Ink.Median([1, 2, 3], 3));
    }
}

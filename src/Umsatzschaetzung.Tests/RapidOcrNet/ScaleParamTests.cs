using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class ScaleParamTests
{
    [Theory]
    [InlineData(1000, 500, 1024, 1024, 512)]
    [InlineData(500, 1000, 1024, 512, 1024)]
    [InlineData(1000, 700, 1024, 1024, 672)]
    [InlineData(1000, 1000, 1000, 960, 960)]
    [InlineData(20, 10, 20, 32, 32)]
    public void TheLegacyScaleCapsTheLongSideAndDropsAnOddSideBelowItsStride(int width, int height, int size, int w, int h)
    {
        using var bitmap = new SKBitmap(width, height);

        var scale = ScaleParam.GetScaleParam(bitmap, size);

        Assert.Equal((width, height, w, h), (scale.SrcWidth, scale.SrcHeight, scale.DstWidth, scale.DstHeight));
        Assert.Equal(w / (float)width, scale.ScaleWidth);
        Assert.Equal(h / (float)height, scale.ScaleHeight);
    }

    [Theory]
    [InlineData(400, 300, 736, 992, 736)]
    [InlineData(300, 400, 736, 736, 992)]
    [InlineData(2496, 3520, 736, 2496, 3520)]
    [InlineData(1000, 800, 736, 992, 800)]
    [InlineData(10, 5, 0, 32, 32)]
    public void TheAdaptiveScaleLiftsTheShortSideToTheLimitAndRoundsToTheNearestStride(int width, int height, int limit, int w, int h)
    {
        using var bitmap = new SKBitmap(width, height);

        var scale = ScaleParam.GetAdaptiveScaleParam(bitmap, limit);

        Assert.Equal((w, h), (scale.DstWidth, scale.DstHeight));
        Assert.Equal(0, scale.DstWidth % 32);
        Assert.Equal(0, scale.DstHeight % 32);
    }

    [Fact]
    public void AnAdaptiveScaleKeepsTheAspectRatioWithinAStride()
    {
        using var bitmap = new SKBitmap(700, 260);
        var scale = ScaleParam.GetAdaptiveScaleParam(bitmap);
        Assert.Equal(736, scale.DstHeight);
        Assert.InRange(scale.DstWidth, 700 * 736 / 260 - 16, 700 * 736 / 260 + 16);
    }
}

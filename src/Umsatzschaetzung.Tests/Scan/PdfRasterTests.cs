using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Scan;

public class PdfRasterTests
{
    const int Max = RapidOcr.MaxImageDimension;

    [Theory]
    [InlineData(595.2756, 841.8898, 300 / 72.0, 2480, 3508)]
    [InlineData(595.2756, 841.8898, 1.0, 595, 842)]
    [InlineData(841.8898, 595.2756, 300 / 72.0, 3508, 2480)]
    [InlineData(960, 400, 1.0, 960, 400)]
    public void APageBelowTheCapRendersAtTheAskedScale(double width, double height, double scale, int w, int h)
    {
        Assert.Equal((w, h), PdfRaster.Target(width, height, scale));
    }

    [Theory]
    [InlineData(3307, 4677, 300 / 72.0)]
    [InlineData(4677, 3307, 300 / 72.0)]
    [InlineData(10000, 100, 1.0)]
    [InlineData(4001, 4001, 1.0)]
    public void AnOversizedPageIsCappedAtTheRecognisersLimitKeepingItsShape(double width, double height, double scale)
    {
        var (w, h) = PdfRaster.Target(width, height, scale);
        Assert.Equal(Max, Math.Max(w, h));
        Assert.InRange((double)w / h, width / height * 0.99, width / height * 1.01);
    }

    [Fact]
    public void APageExactlyAtTheCapIsNotScaled()
    {
        Assert.Equal((Max, 2000), PdfRaster.Target(Max, 2000, 1.0));
    }

    [Theory]
    [InlineData(0.1, 100, 1.0, 1, 100)]
    [InlineData(100000, 1, 1.0, Max, 1)]
    [InlineData(0.0, 0.0, 1.0, 1, 1)]
    public void ASideNeverRendersBelowOnePixel(double width, double height, double scale, int w, int h)
    {
        Assert.Equal((w, h), PdfRaster.Target(width, height, scale));
    }
}

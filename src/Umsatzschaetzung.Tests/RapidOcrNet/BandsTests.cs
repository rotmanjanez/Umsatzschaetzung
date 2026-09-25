using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class BandsTests
{
    static SKBitmap Noise(int w, int h)
    {
        var random = new Random(7);
        var page = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
        var pixels = page.GetPixelSpan();
        random.NextBytes(pixels);
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        page.NotifyPixelsChanged();
        return page;
    }

    [Theory]
    [InlineData(301, 407, 320, 416)]
    [InlineData(301, 407, 96, 128)]
    [InlineData(250, 130, 250, 131)]
    public void ResizesByteForByteLikeSkia(int w, int h, int toW, int toH)
    {
        using var page = Noise(w, h);
        foreach (var sampling in new[] { new SKSamplingOptions(SKCubicResampler.Mitchell), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None) })
        {
            var info = page.Info.WithSize(toW, toH);
            using var expected = page.Resize(info, sampling);
            using var banded = Bands.Resize(page, info, sampling);
            Assert.True(expected.GetPixelSpan().SequenceEqual(banded.GetPixelSpan()));
        }
    }
}

using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class OcrUtilsTests
{
    static readonly float[] Mean = [1f, 2f, 3f];
    static readonly float[] Norm = [0.5f, 0.25f, 2f];

    [Fact]
    public void NormalisationLaysOutOneBatchOfThreePlanesInBgrOrder()
    {
        using var bitmap = Images.Blank(3, 2, new SKColor(0, 0, 0));
        bitmap.SetPixel(1, 0, new SKColor(10, 20, 30));

        var tensor = OcrUtils.SubtractMeanNormalize(bitmap, Mean, Norm);

        Assert.Equal([1, 3, 2, 3], tensor.Dimensions.ToArray());
        Assert.Equal((30 - 1) * 0.5f, tensor[0, 0, 0, 1]);
        Assert.Equal((20 - 2) * 0.25f, tensor[0, 1, 0, 1]);
        Assert.Equal((10 - 3) * 2f, tensor[0, 2, 0, 1]);
        Assert.Equal(-0.5f, tensor[0, 0, 1, 2]);
    }

    [Fact]
    public void NormalisationFollowsTheRowStrideOfASubset()
    {
        using var source = Images.Coded(4, 3);
        using var subset = new SKBitmap();
        Assert.True(source.ExtractSubset(subset, new SKRectI(1, 1, 3, 3)));
        Assert.True(subset.RowBytes > subset.Width * 4);

        var tensor = OcrUtils.SubtractMeanNormalize(subset, [0f, 0f, 0f], [1f, 1f, 1f]);

        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 2; x++)
            {
                var c = Images.Code(x + 1, y + 1);
                Assert.Equal(c.Blue, tensor[0, 0, y, x]);
                Assert.Equal(c.Green, tensor[0, 1, y, x]);
                Assert.Equal(c.Red, tensor[0, 2, y, x]);
            }
    }

    [Fact]
    public void NormalisationSpreadsAGreyPixelOverAllThreeChannels()
    {
        using var gray = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Gray8, SKAlphaType.Opaque));
        gray.Erase(new SKColor(100, 100, 100));

        var tensor = OcrUtils.SubtractMeanNormalize(gray, Mean, Norm);

        Assert.Equal([1, 3, 2, 2], tensor.Dimensions.ToArray());
        Assert.Equal(49.5f, tensor[0, 0, 1, 1]);
        Assert.Equal(24.5f, tensor[0, 1, 1, 1]);
        Assert.Equal(194f, tensor[0, 2, 1, 1]);
    }

    [Fact]
    public void NormalisationRejectsAnyOtherPixelLayout()
    {
        using var rgba = new SKBitmap(new SKImageInfo(2, 2, SKColorType.Rgba8888, SKAlphaType.Premul));
        Assert.Throws<ArgumentException>(() => OcrUtils.SubtractMeanNormalize(rgba, Mean, Norm));
    }

    [Theory]
    [InlineData(96, 64)]
    [InlineData(640, 480)]
    public void ABitmapWithinBoundsOnTheStrideIsReturnedAsIs(int width, int height)
    {
        using var bitmap = Images.Blank(width, height);
        var bounded = OcrUtils.ResizeImageWithinBounds(bitmap, 30, 2000, out var owned);
        Assert.Same(bitmap, bounded);
        Assert.False(owned);
    }

    [Theory]
    [InlineData(4000, 3000, 30, 2000, 1984, 1504)]
    [InlineData(300, 20, 30, 2000, 448, 32)]
    [InlineData(100, 64, 30, 2000, 96, 64)]
    [InlineData(5000, 100, 0, 0, 4992, 96)]
    public void BoundsScaleTheImageAndRoundBothSidesToTheStride(int width, int height, int min, int max, int w, int h)
    {
        using var bitmap = Images.Blank(width, height);
        using var bounded = OcrUtils.ResizeImageWithinBounds(bitmap, min, max, out var owned);
        Assert.True(owned);
        Assert.NotSame(bitmap, bounded);
        Assert.Equal((w, h), (bounded.Width, bounded.Height));
    }

    [Fact]
    public void ALongPageKeepsItsAspectRatioWithinTheStride()
    {
        using var bitmap = Images.Blank(2480, 3508);
        using var bounded = OcrUtils.ResizeImageWithinBounds(bitmap, 30, 2000, out _);
        Assert.InRange(bounded.Height, 1984, 2016);
        Assert.InRange(bounded.Width / (double)bounded.Height, 2480 / 3508.0 - 0.02, 2480 / 3508.0 + 0.02);
    }

    [Fact]
    public void AWideStripIsLetterboxedTopAndBottomInWhite()
    {
        using var strip = Images.Blank(400, 40, SKColors.Black);

        using var padded = OcrUtils.ApplyVerticalLetterbox(strip, 8f, 30, out var top);

        Assert.Equal(30, top);
        Assert.Equal((400, 100), (padded.Width, padded.Height));
        Assert.Equal(SKColors.White, padded.GetPixel(200, 10));
        Assert.Equal(SKColors.Black, padded.GetPixel(200, 50));
        Assert.Equal(SKColors.White, padded.GetPixel(200, 90));
    }

    [Fact]
    public void AShortStripIsLetterboxedToTwiceTheMinimumHeightWhenTheRatioIsOff()
    {
        using var strip = Images.Blank(100, 20);
        using var padded = OcrUtils.ApplyVerticalLetterbox(strip, -1f, 30, out var top);
        Assert.Equal(20, top);
        Assert.Equal(60, padded.Height);
    }

    [Fact]
    public void AnImageOfOrdinaryProportionsIsNotLetterboxed()
    {
        using var page = Images.Blank(100, 50);
        Assert.Same(page, OcrUtils.ApplyVerticalLetterbox(page, 8f, 30, out var top));
        Assert.Equal(0, top);
    }

    [Fact]
    public void PaddingAddsAWhiteBorderAroundTheUnchangedImage()
    {
        using var image = Images.Blank(4, 3, SKColors.Black);

        using var padded = OcrUtils.MakePadding(image, 5);

        Assert.Equal((14, 13), (padded.Width, padded.Height));
        Assert.Equal(SKColors.White, padded.GetPixel(0, 0));
        Assert.Equal(SKColors.White, padded.GetPixel(4, 6));
        Assert.Equal(SKColors.Black, padded.GetPixel(5, 5));
        Assert.Equal(SKColors.Black, padded.GetPixel(8, 7));
        Assert.Equal(SKColors.White, padded.GetPixel(9, 7));
    }

    [Fact]
    public void NoPaddingReturnsTheSameBitmap()
    {
        using var image = Images.Blank(4, 3);
        Assert.Same(image, OcrUtils.MakePadding(image, 0));
    }

    [Theory]
    [InlineData(500, 300, 2)]
    [InlineData(999, 1500, 2)]
    [InlineData(3000, 2000, 4)]
    public void ThicknessGrowsWithTheShorterSide(int width, int height, int thickness)
    {
        using var image = new SKBitmap(width, height);
        Assert.Equal(thickness, OcrUtils.GetThickness(image));
    }

    [Fact]
    public void ThePerspectiveTransformMapsTheQuadOntoTheRectangle()
    {
        SKPointI tl = new(10, 20), tr = new(110, 30), br = new(105, 80), bl = new(5, 70);

        var m = OcrUtils.GetPerspectiveTransform(tl, tr, br, bl, 100, 50);

        AssertNear(new SKPoint(0, 0), m.MapPoint(tl.X, tl.Y));
        AssertNear(new SKPoint(100, 0), m.MapPoint(tr.X, tr.Y));
        AssertNear(new SKPoint(100, 50), m.MapPoint(br.X, br.Y));
        AssertNear(new SKPoint(0, 50), m.MapPoint(bl.X, bl.Y));
    }

    [Fact]
    public void AnAxisAlignedBoxIsCroppedPixelForPixel()
    {
        using var source = Images.Blank(60, 40);
        for (var y = 0; y < 40; y++)
            for (var x = 0; x < 60; x++)
                source.SetPixel(x, y, new SKColor((byte)(x * 4), (byte)(y * 6), 90));
        SKPointI[] box = [new(10, 5), new(40, 5), new(40, 25), new(10, 25)];

        using var crop = OcrUtils.GetRotateCropImage(source, box, out var context);

        Assert.Equal((30, 20), (crop.Width, crop.Height));
        for (var y = 0; y < 20; y++)
            for (var x = 0; x < 30; x++)
                Assert.Equal(source.GetPixel(x + 10, y + 5), crop.GetPixel(x, y));
        Assert.Equal((10, 5, 30, 20), (context.Left, context.Top, context.PartImgWidth, context.PartImgHeight));
        Assert.False(context.HasPerspective);
        Assert.False(context.Rotated90);
    }

    [Fact]
    public void ARotatedBoxComesOutUpright()
    {
        using var source = Images.Blank(200, 200);
        using (var canvas = new SKCanvas(source))
        {
            canvas.Translate(100, 100);
            canvas.RotateDegrees(30);
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(-60, -15, 60, 30, red);
            canvas.DrawRect(0, -15, 60, 30, blue);
        }
        var box = Corners(100, 100, 30, 120, 30);

        using var crop = OcrUtils.GetRotateCropImage(source, box, out var context);

        Assert.InRange(crop.Width, 118, 120);
        Assert.InRange(crop.Height, 28, 30);
        Assert.True(context.HasPerspective);
        Assert.False(context.Rotated90);
        AssertColor(SKColors.Red, crop.GetPixel(crop.Width / 4, crop.Height / 2));
        AssertColor(SKColors.Blue, crop.GetPixel(crop.Width * 3 / 4, crop.Height / 2));
    }

    [Fact]
    public void ATallCropIsTurnedAQuarterClockwiseOnlyWhenAsked()
    {
        using var source = Images.Blank(100, 100);
        Images.Fill(source, 20, 10, 30, 25, SKColors.Red);
        SKPointI[] box = [new(20, 10), new(30, 10), new(30, 40), new(20, 40)];

        using (var turned = OcrUtils.GetRotateCropImage(source, box, out var context))
        {
            Assert.Equal((30, 10), (turned.Width, turned.Height));
            Assert.True(context.Rotated90);
            Assert.Equal((10, 30), (context.PartImgWidth, context.PartImgHeight));
            AssertColor(SKColors.Red, turned.GetPixel(25, 5));
            AssertColor(SKColors.White, turned.GetPixel(3, 5));
        }

        using (var upright = OcrUtils.GetRotateCropImage(source, box, out var context, rotateTall: false))
        {
            Assert.Equal((10, 30), (upright.Width, upright.Height));
            Assert.False(context.Rotated90);
            AssertColor(SKColors.Red, upright.GetPixel(5, 5));
        }
    }

    [Fact]
    public void ACropJustUnderOneAndAHalfTimesItsWidthStaysUpright()
    {
        using var source = Images.Blank(100, 100);
        SKPointI[] box = [new(0, 0), new(20, 0), new(20, 29), new(0, 29)];
        using var crop = OcrUtils.GetRotateCropImage(source, box, out var context);
        Assert.False(context.Rotated90);
        Assert.Equal((20, 29), (crop.Width, crop.Height));
    }

    [Fact]
    public void ABoxOutsideTheImageCannotBeCropped()
    {
        using var source = Images.Blank(50, 50);
        SKPointI[] box = [new(60, 60), new(90, 60), new(90, 70), new(60, 70)];
        Assert.Throws<InvalidOperationException>(() => OcrUtils.GetRotateCropImage(source, box));
    }

    [Fact]
    public void TurningClockwiseMovesEveryPixelAQuarterRound()
    {
        using var source = Images.Coded(3, 2);
        using var turned = OcrUtils.BitmapRotateClockWise90(source);
        Assert.Equal((2, 3), (turned.Width, turned.Height));
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 3; x++)
                AssertClosest(x, y, turned.GetPixel(1 - y, x));
    }

    [Fact]
    public void TurningCounterClockwiseMovesEveryPixelAQuarterRoundBack()
    {
        using var source = Images.Coded(3, 2);
        using var turned = OcrUtils.BitmapRotateCounterClockWise90(source);
        Assert.Equal((2, 3), (turned.Width, turned.Height));
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 3; x++)
                AssertClosest(x, y, turned.GetPixel(y, 2 - x));
    }

    [Fact]
    public void TurningHalfWayMirrorsBothAxes()
    {
        using var source = Images.Coded(3, 2);
        using var turned = OcrUtils.BitmapRotateClockWise180(source);
        Assert.Equal((3, 2), (turned.Width, turned.Height));
        for (var y = 0; y < 2; y++)
            for (var x = 0; x < 3; x++)
                AssertClosest(x, y, turned.GetPixel(2 - x, 1 - y));
    }

    [Fact]
    public void PartImagesAreCutPerBoxWithTheirContexts()
    {
        using var source = Images.Blank(100, 100);
        TextBox[] boxes =
        [
            new() { BoxPoints = [new(0, 0), new(40, 0), new(40, 10), new(0, 10)] },
            new() { BoxPoints = [new(50, 20), new(60, 20), new(60, 80), new(50, 80)] },
        ];

        var parts = OcrUtils.GetPartImages(source, boxes);
        var (withContext, contexts) = OcrUtils.GetPartImagesWithContext(source, boxes, rotateTall: false);
        try
        {
            Assert.Equal([(40, 10), (60, 10)], parts.Select(p => (p.Width, p.Height)));
            Assert.Equal([(40, 10), (10, 60)], withContext.Select(p => (p.Width, p.Height)));
            Assert.Equal([(0, 0), (50, 20)], contexts.Select(c => (c.Left, c.Top)));
            Assert.All(contexts, c => Assert.False(c.Rotated90));
        }
        finally
        {
            foreach (var p in parts.Concat(withContext)) p.Dispose();
        }
    }

    [Fact]
    public void NoBoxesMeanNoPartImages()
    {
        using var source = Images.Blank(10, 10);
        Assert.Empty(OcrUtils.GetPartImages(source, null));
        Assert.Empty(OcrUtils.GetPartImages(source, []));
        var (parts, contexts) = OcrUtils.GetPartImagesWithContext(source, []);
        Assert.Empty(parts);
        Assert.Empty(contexts);
    }

    [Fact]
    public void ABadBoxAmongGoodOnesFailsTheWholeCut()
    {
        using var source = Images.Blank(50, 50);
        TextBox[] boxes =
        [
            new() { BoxPoints = [new(0, 0), new(20, 0), new(20, 10), new(0, 10)] },
            new() { BoxPoints = [new(60, 60), new(90, 60), new(90, 70), new(60, 70)] },
        ];
        Assert.Throws<InvalidOperationException>(() => OcrUtils.GetPartImages(source, boxes));
        Assert.Throws<InvalidOperationException>(() => OcrUtils.GetPartImagesWithContext(source, boxes));
    }

    internal static SKPointI[] Corners(float cx, float cy, float degrees, float width, float height)
    {
        var m = SKMatrix.CreateRotationDegrees(degrees, cx, cy);
        SKPoint[] corners =
        [
            new(cx - width / 2, cy - height / 2), new(cx + width / 2, cy - height / 2),
            new(cx + width / 2, cy + height / 2), new(cx - width / 2, cy + height / 2),
        ];
        return [.. m.MapPoints(corners).Select(p => new SKPointI((int)MathF.Round(p.X), (int)MathF.Round(p.Y)))];
    }

    // The turns sample through a Mitchell filter, which is not interpolating: each pixel keeps
    // about 90 % of itself and takes the rest from its neighbours.
    static void AssertClosest(int x, int y, SKColor actual)
    {
        static int Gap(SKColor a, SKColor b) => Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);
        var nearest = Enumerable.Range(0, 3).SelectMany(cx => Enumerable.Range(0, 2).Select(cy => (cx, cy)))
            .MinBy(c => Gap(Images.Code(c.cx, c.cy), actual));
        Assert.Equal((x, y), nearest);
    }

    static void AssertNear(SKPoint expected, SKPoint actual)
    {
        Assert.Equal(expected.X, actual.X, 0.01f);
        Assert.Equal(expected.Y, actual.Y, 0.01f);
    }

    static void AssertColor(SKColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 40);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 40);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 40);
    }
}

using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class PContourTests
{
    static int[] Mask(int width, int height, params (int X0, int Y0, int X1, int Y1)[] blobs)
    {
        var mask = new int[width * height];
        foreach (var (x0, y0, x1, y1) in blobs)
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    mask[y * width + x] = 1;
        return mask;
    }

    static (float X0, float Y0, float X1, float Y1) Bounds(PContour.Contour c) =>
        (c.points.Min(p => p.X), c.points.Min(p => p.Y), c.points.Max(p => p.X), c.points.Max(p => p.Y));

    static float Area(IReadOnlyList<SKPoint> polygon)
    {
        float sum = 0;
        for (var i = 0; i < polygon.Count; i++)
        {
            var (a, b) = (polygon[i], polygon[(i + 1) % polygon.Count]);
            sum += a.X * b.Y - b.X * a.Y;
        }
        return MathF.Abs(sum) / 2;
    }

    [Fact]
    public void ABlockHasOneOuterContourAlongItsBorder()
    {
        var mask = Mask(12, 10, (3, 2, 8, 6));

        var contours = PContour.FindContours(mask, 12, 10);

        var contour = Assert.Single(contours);
        Assert.False(contour.isHole);
        Assert.Equal(0, contour.parent);
        Assert.Equal((3f, 2f, 8f, 6f), Bounds(contour));
        Assert.Equal(5f * 4f, Area(contour.points));
    }

    [Fact]
    public void SeparateBlobsAreSeparateContours()
    {
        var mask = Mask(20, 10, (2, 2, 5, 5), (10, 3, 16, 7));

        var contours = PContour.FindContours(mask, 20, 10);

        Assert.Equal(2, contours.Count);
        Assert.All(contours, c => Assert.False(c.isHole));
        Assert.Equal([(2f, 2f, 5f, 5f), (10f, 3f, 16f, 7f)], contours.Select(Bounds).OrderBy(b => b.X0));
    }

    [Fact]
    public void ARingHasAnOuterContourAndAHoleInsideIt()
    {
        var mask = Mask(12, 12, (2, 2, 9, 9));
        for (var y = 4; y <= 7; y++)
            for (var x = 4; x <= 7; x++)
                mask[y * 12 + x] = 0;

        var contours = PContour.FindContours(mask, 12, 12);

        Assert.Equal(2, contours.Count);
        var outer = Assert.Single(contours, c => !c.isHole);
        var hole = Assert.Single(contours, c => c.isHole);
        Assert.Equal(outer.id, hole.parent);
        Assert.Equal((2f, 2f, 9f, 9f), Bounds(outer));
        Assert.Equal((3f, 3f, 8f, 8f), Bounds(hole));
    }

    [Fact]
    public void InkOnTheFrameIsIgnored()
    {
        var mask = Mask(8, 8, (0, 0, 7, 0), (0, 0, 0, 7));
        Assert.Empty(PContour.FindContours(mask, 8, 8));
    }

    [Fact]
    public void DouglasPeuckerReducesANoisySquareToItsCorners()
    {
        var outline = new List<SKPoint>();
        var jitter = new[] { 0.3f, -0.2f, 0.1f, -0.3f, 0.2f };
        for (var i = 0; i < 40; i++)
        {
            var d = jitter[i % jitter.Length];
            outline.Add(i switch
            {
                < 10 => new SKPoint(i * 10, d),
                < 20 => new SKPoint(100 + d, (i - 10) * 10),
                < 30 => new SKPoint(100 - (i - 20) * 10, 100 + d),
                _ => new SKPoint(d, 100 - (i - 30) * 10),
            });
        }
        outline[0] = new SKPoint(0, 0);
        outline.Add(new SKPoint(0, 0));
        outline[10] = new SKPoint(100, 0);
        outline[20] = new SKPoint(100, 100);
        outline[30] = new SKPoint(0, 100);

        var reduced = PContour.ApproxPolyDP(outline.ToArray(), 1).ToArray();

        Assert.Equal(
            new SKPoint[] { new(0, 0), new(0, 100), new(100, 0), new(100, 100) },
            reduced.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y));
    }

    [Fact]
    public void TheSimpleReductionDropsOnlyPointsOnAStraightRun()
    {
        SKPoint[] line = [new(0, 0), new(1, 0), new(2, 0), new(3, 0), new(3, 1), new(3, 2), new(2, 3)];

        var reduced = PContour.ApproxPolySimple(line).ToArray();

        Assert.Equal(new SKPoint[] { new(0, 0), new(3, 0), new(3, 2), new(2, 3) }, reduced);
    }

    [Fact]
    public void TwoPointsCannotBeReducedFurther()
    {
        SKPoint[] two = [new(0, 0), new(5, 5)];
        Assert.Equal(two, PContour.ApproxPolyDP(two, 1).ToArray());
        Assert.Equal(two, PContour.ApproxPolySimple(two).ToArray());
    }
}

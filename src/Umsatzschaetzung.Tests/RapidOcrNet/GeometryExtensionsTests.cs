using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class GeometryExtensionsTests
{
    [Fact]
    public void TheHullKeepsOnlyTheCornersOfASquare()
    {
        SKPoint[] points = [new(0, 0), new(5, 5), new(10, 0), new(3, 7), new(10, 10), new(0, 10), new(5, 0), new(10, 4), new(0, 6)];

        var hull = GeometryExtensions.GrahamScan(points);

        Assert.Equal(
            new SKPoint[] { new(0, 0), new(0, 10), new(10, 0), new(10, 10) }.OrderBy(p => p.X).ThenBy(p => p.Y),
            hull.OrderBy(p => p.X).ThenBy(p => p.Y));
    }

    [Fact]
    public void TheHullOfCollinearPointsIsItsTwoEnds()
    {
        SKPoint[] points = [new(2, 2), new(0, 0), new(4, 4), new(1, 1), new(3, 3)];
        var hull = GeometryExtensions.GrahamScan(points);
        Assert.Equal([new SKPoint(0, 0), new SKPoint(4, 4)], hull.OrderBy(p => p.X));
    }

    [Fact]
    public void FewerThanThreePointsAreTheirOwnHull()
    {
        SKPoint[] two = [new(1, 2), new(3, 4)];
        Assert.Same(two, GeometryExtensions.GrahamScan(two));
        Assert.Throws<ArgumentException>(() => GeometryExtensions.GrahamScan([]));
    }

    [Fact]
    public void TheMinimumRectangleOfAnAxisAlignedCloudIsItsBoundingBox()
    {
        SKPoint[] points = [new(0, 0), new(10, 0), new(10, 5), new(0, 5), new(3, 2), new(7, 4), new(5, 0)];

        var rect = GeometryExtensions.MinimumAreaRectangle(points);

        Assert.Equal(4, rect.Length);
        Assert.Equal(
            new SKPoint[] { new(0, 0), new(0, 5), new(10, 0), new(10, 5) },
            rect.Select(Round).OrderBy(p => p.X).ThenBy(p => p.Y));
        GeometryExtensions.GetSize(rect, out var width, out var height);
        Assert.Equal((5f, 10f), (MathF.Min(width, height), MathF.Max(width, height)));
    }

    [Fact]
    public void TheMinimumRectangleOfARotatedRectangleIsThatRectangle()
    {
        var corners = OcrUtilsTests.Corners(100, 100, 25, 80, 20).Select(p => new SKPoint(p.X, p.Y)).ToArray();
        SKPoint[] points = [.. corners, new(100, 100), new(110, 102)];

        var rect = GeometryExtensions.MinimumAreaRectangle(points);

        foreach (var corner in corners)
            Assert.Contains(rect, p => SKPoint.Distance(p, corner) < 1.5f);
        GeometryExtensions.GetSize(rect, out var width, out var height);
        Assert.Equal(20f, MathF.Min(width, height), 1.5f);
        Assert.Equal(80f, MathF.Max(width, height), 1.5f);
    }

    [Fact]
    public void ADegenerateRectangleHasNoSize()
    {
        var line = GeometryExtensions.MinimumAreaRectangle([new(0, 0), new(4, 4), new(2, 2)]);
        Assert.Equal(2, line.Length);
        GeometryExtensions.GetSize(line, out var width, out var height);
        Assert.True(float.IsNaN(width) && float.IsNaN(height));
    }

    static SKPoint Round(SKPoint p) => new(MathF.Round(p.X, 3), MathF.Round(p.Y, 3));
}

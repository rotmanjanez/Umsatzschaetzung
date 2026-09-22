using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class SplitStackedBoxesTests
{
    static TextBox Box(int x0, int y0, int x1, int y1, float score = 0.8f) =>
        new() { Score = score, BoxPoints = [new(x0, y0), new(x1, y0), new(x1, y1), new(x0, y1)] };

    static SKBitmap Stack(params (int Top, int Bottom)[] bands)
    {
        var page = Images.Blank(200, 300);
        foreach (var (top, bottom) in bands)
            Images.Fill(page, 50, top, 80, bottom, SKColors.Black);
        return page;
    }

    [Fact]
    public void AColumnOfThreeLinesIsCutAtTheGaps()
    {
        using var page = Stack((20, 40), (60, 80), (100, 120));
        var stack = Box(45, 10, 85, 130, 0.7f);

        var split = OcrUtils.SplitStackedBoxes(page, [stack]);

        Assert.Equal([(20, 40), (60, 80), (100, 120)], split.Select(b => (b.BoxPoints[0].Y, b.BoxPoints[2].Y)));
        Assert.All(split, b =>
        {
            Assert.Equal(0.7f, b.Score);
            Assert.Equal((45, 85), (b.BoxPoints[0].X, b.BoxPoints[1].X));
            Assert.Equal(b.BoxPoints[0].X, b.BoxPoints[3].X);
            Assert.Equal(b.BoxPoints[1].X, b.BoxPoints[2].X);
        });
    }

    [Fact]
    public void TheBandsTakeTheStacksPlaceAmongTheOtherBoxes()
    {
        using var page = Stack((20, 40), (60, 80));
        var before = Box(100, 10, 190, 30);
        var after = Box(100, 200, 190, 220);

        var split = OcrUtils.SplitStackedBoxes(page, [before, Box(45, 10, 85, 90), after]);

        Assert.Equal(4, split.Count);
        Assert.Same(before, split[0]);
        Assert.Equal(20, split[1].BoxPoints[0].Y);
        Assert.Equal(60, split[2].BoxPoints[0].Y);
        Assert.Same(after, split[3]);
    }

    [Fact]
    public void ATallBoxWithOneBandOfInkIsLeftAlone()
    {
        using var page = Stack((20, 100));
        IReadOnlyList<TextBox> boxes = [Box(45, 10, 85, 110)];
        Assert.Same(boxes, OcrUtils.SplitStackedBoxes(page, boxes));
    }

    [Fact]
    public void AWideBoxIsNeverCut()
    {
        using var page = Images.Blank(300, 100);
        Images.Fill(page, 10, 10, 290, 30, SKColors.Black);
        Images.Fill(page, 10, 50, 290, 70, SKColors.Black);
        IReadOnlyList<TextBox> boxes = [Box(5, 5, 295, 75)];
        Assert.Same(boxes, OcrUtils.SplitStackedBoxes(page, boxes));
    }

    [Fact]
    public void ALeaningBoxIsALineAtAnAngleNotAStack()
    {
        using var page = Stack((20, 40), (60, 80), (100, 120));
        IReadOnlyList<TextBox> boxes = [new() { BoxPoints = [new(45, 10), new(85, 30), new(85, 130), new(45, 110)] }];
        Assert.Same(boxes, OcrUtils.SplitStackedBoxes(page, boxes));
    }

    [Fact]
    public void AFaintBoxWithoutContrastIsLeftAlone()
    {
        using var page = Images.Blank(200, 300);
        Images.Fill(page, 50, 20, 80, 40, new SKColor(230, 230, 230));
        Images.Fill(page, 50, 60, 80, 80, new SKColor(230, 230, 230));
        IReadOnlyList<TextBox> boxes = [Box(45, 10, 85, 90)];
        Assert.Same(boxes, OcrUtils.SplitStackedBoxes(page, boxes));
    }

    [Fact]
    public void AFragmentPulledInFromTheNextRowIsNotALine()
    {
        using var page = Stack((20, 60), (80, 90));
        IReadOnlyList<TextBox> boxes = [Box(45, 10, 85, 95)];
        Assert.Same(boxes, OcrUtils.SplitStackedBoxes(page, boxes));
    }

    [Fact]
    public void AFragmentBesideTwoLinesIsDroppedFromTheCut()
    {
        using var page = Stack((12, 16), (20, 40), (60, 80), (112, 120));
        var split = OcrUtils.SplitStackedBoxes(page, [Box(45, 10, 85, 130)]);
        Assert.Equal([20, 60], split.Select(b => b.BoxPoints[0].Y));
    }

    // The detector ran three lines together and its unclip reached two thirds into the lines
    // above and below, which have boxes of their own: those parts are not read a second time.
    [Fact]
    public void ALineCutOffByTheEdgeOfTheBoxBelongsToTheNeighbouringBox()
    {
        using var page = Stack((0, 30), (40, 60), (70, 90), (100, 120), (130, 160));

        var split = OcrUtils.SplitStackedBoxes(page, [Box(45, 16, 85, 144)]);

        Assert.Equal([(40, 60), (70, 90), (100, 120)], split.Select(b => (b.BoxPoints[0].Y, b.BoxPoints[2].Y)));
    }

    [Fact]
    public void ABoxRunningOverTheImageEdgesIsClampedToIt()
    {
        using var page = Images.Blank(40, 100);
        Images.Fill(page, 5, 0, 35, 30, SKColors.Black);
        Images.Fill(page, 5, 70, 35, 100, SKColors.Black);

        var split = OcrUtils.SplitStackedBoxes(page, [Box(-5, -10, 45, 110)]);

        Assert.Equal([(0, 30), (70, 100)], split.Select(b => (b.BoxPoints[0].Y, b.BoxPoints[2].Y)));
        Assert.All(split, b => Assert.Equal((0, 39), (b.BoxPoints[0].X, b.BoxPoints[1].X)));
    }

    [Fact]
    public void NoBoxesStayNoBoxes()
    {
        using var page = Images.Blank(10, 10);
        IReadOnlyList<TextBox> none = [];
        Assert.Same(none, OcrUtils.SplitStackedBoxes(page, none));
    }

    [Fact]
    public void EachBandIsCroppedToItsOwnLine()
    {
        using var page = Stack((20, 40), (60, 80));
        var split = OcrUtils.SplitStackedBoxes(page, [Box(45, 10, 85, 90)]);

        var parts = OcrUtils.GetPartImages(page, split, rotateTall: false);
        try
        {
            Assert.All(parts, p =>
            {
                Assert.Equal(20, p.Height);
                Assert.Equal(SKColors.Black, p.GetPixel(20, 0));
                Assert.Equal(SKColors.Black, p.GetPixel(20, 19));
            });
        }
        finally
        {
            foreach (var p in parts) p.Dispose();
        }
    }
}

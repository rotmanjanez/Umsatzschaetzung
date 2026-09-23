using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

[Collection(OcrCollection.Name)]
public class RapidOcrTests(OcrModels models)
{
    static void AssertReads(string expected, string actual) =>
        Assert.True(Images.Distance(expected, actual.Trim()) <= 2, $"read '{actual}' for '{expected}'");

    static void AssertInside((int X0, int Y0, int X1, int Y1) inner, (int X0, int Y0, int X1, int Y1) outer, int slack = 2)
    {
        Assert.InRange(inner.X0, outer.X0 - slack, outer.X1);
        Assert.InRange(inner.X1, outer.X0, outer.X1 + slack);
        Assert.InRange(inner.Y0, outer.Y0 - slack, outer.Y1);
        Assert.InRange(inner.Y1, outer.Y0, outer.Y1 + slack);
    }

    [Fact]
    public void ThreePrintedLinesAreReadBackTopToBottom()
    {
        var blocks = models.Read(false, OcrModels.App).TextBlocks;

        Assert.Equal(3, blocks.Length);
        for (var i = 0; i < 3; i++)
        {
            AssertReads(OcrModels.Lines[i], blocks[i].Text);
            var box = Images.Bounds(blocks[i].BoxPoints);
            Assert.InRange(OcrModels.Baselines[i], box.Y0, box.Y1);
            Assert.Equal(0, blocks[i].AngleIndex);
        }
        Assert.True(blocks.Select(b => Images.Bounds(b.BoxPoints).Y0).SequenceEqual(blocks.Select(b => Images.Bounds(b.BoxPoints).Y0).Order()));
    }

    [Fact]
    public void EveryWordLiesInsideItsLineInReadingOrder()
    {
        foreach (var block in models.Read(false, OcrModels.App).TextBlocks)
        {
            var words = block.WordResults;
            Assert.NotNull(words);
            Assert.Equal(block.Text.Replace(" ", ""), string.Concat(words.Select(w => w.Text)));
            Assert.All(words, w => AssertInside(Images.Bounds(w.BoxPoints), Images.Bounds(block.BoxPoints)));
            Assert.True(words.Select(w => w.BoxPoints[0].X).SequenceEqual(words.Select(w => w.BoxPoints[0].X).Order()));
        }
    }

    [Fact]
    public void AnUpsideDownPageIsReadUprightWhenTheClassifierMayTurnCrops()
    {
        var blocks = models.Read(true, OcrModels.Upstream).TextBlocks;

        Assert.Equal(3, blocks.Length);
        for (var i = 0; i < 3; i++)
        {
            AssertReads(OcrModels.Lines[2 - i], blocks[i].Text);
            Assert.Equal(1, blocks[i].AngleIndex);
        }
        var invoice = blocks[2].WordResults!;
        Assert.True(invoice[0].BoxPoints[0].X > invoice[^1].BoxPoints[0].X, "the first word of a turned line sits on its right");
        Assert.All(invoice, w => AssertInside(Images.Bounds(w.BoxPoints), Images.Bounds(blocks[2].BoxPoints)));
    }

    // The page as a whole is turned by the caller, from the verdicts: a line read upside down
    // reads as garbage or as nothing, and dropping it for that would drop its verdict with it.
    [Fact]
    public void WithoutTurningCropsAnUpsideDownPageStillReportsItsVerdictOnEveryLine()
    {
        var blocks = models.Read(true, OcrModels.App).TextBlocks;

        Assert.Equal(3, blocks.Length);
        Assert.All(blocks, b => Assert.Equal(1, b.AngleIndex));
        Assert.All(blocks, b => Assert.DoesNotContain(OcrModels.Lines, l => Images.Distance(l, b.Text.Trim()) <= 2));
    }

    [Fact]
    public void AnUprightPageReadsTheSameWhetherCropsMayBeTurnedOrNot()
    {
        var app = models.Read(false, OcrModels.App).TextBlocks;
        var upstream = models.Read(false, OcrModels.Upstream).TextBlocks;
        Assert.Equal(upstream.Select(b => b.Text), app.Select(b => b.Text));
    }

    [Fact]
    public void AColumnOfShortTokensThatTheDetectorRanTogetherIsReadLineByLine()
    {
        const int gap = 24;
        var baselines = Enumerable.Range(0, 5).Select(i => 50f + i * gap).ToArray();
        using var column = Images.Text(300, 60 + 5 * gap, 40, [("Artikel", 20, 50), .. baselines.Select(b => ("ca", 200f, b))]);

        var joined = models.Engine.Detect(column, OcrModels.App with { SplitStackedCrops = false }).TextBlocks;
        if (joined.Count(b => b.Text == "ca") == 5)
            Assert.Skip("The detector kept the column apart on this machine's rendering.");

        var split = models.Engine.Detect(column, OcrModels.App).TextBlocks;

        var units = split.Where(b => b.Text == "ca").Select(b => Images.Bounds(b.BoxPoints)).OrderBy(b => b.Y0).ToArray();
        Assert.Equal(5, units.Length);
        for (var i = 0; i < 5; i++)
            Assert.InRange(baselines[i], units[i].Y0, units[i].Y1 + 2);
    }

    [Fact]
    public void AWhitePageHasNothingToRead()
    {
        using var blank = Images.Blank(400, 300);

        var result = models.Engine.Detect(blank, OcrModels.App);

        Assert.Empty(result.TextBlocks);
        Assert.Equal("", result.StrRes);
        Assert.Empty(models.Engine.DetectBoxes(blank, OcrModels.App));
    }

    [Fact]
    public void ASliverFarTallerThanWideIsReadWithoutFailing()
    {
        using var sliver = Images.Blank(2, 200);

        var line = models.Recognizer.GetTextLine(sliver);

        Assert.Empty(line.Chars ?? []);
    }

    [Fact]
    public void DetectedBoxesAreTheBoxesTheLinesAreReadFrom()
    {
        var boxes = models.Engine.DetectBoxes(models.Page, OcrModels.App);
        var blocks = models.Read(false, OcrModels.App).TextBlocks;
        Assert.Equal(blocks.Select(b => Images.Bounds(b.BoxPoints)), boxes.Select(b => Images.Bounds(b.BoxPoints)));
    }

    [Fact]
    public void TheDetectorBoxesEachLineAroundItsInk()
    {
        var scale = ScaleParam.GetAdaptiveScaleParam(models.Page);

        var boxes = models.Detector.GetTextBoxes(models.Page, scale, 0.5f, 0.3f, 1.6f);

        Assert.NotNull(boxes);
        Assert.Equal(3, boxes.Count);
        for (var i = 0; i < 3; i++)
            AssertInside(Ink(models.Page, OcrModels.Baselines[i] - 40, OcrModels.Baselines[i] + 15), Images.Bounds(boxes[i].BoxPoints));
    }

    [Fact]
    public void TheClassifierTellsAnUprightLineFromAnUpsideDownOne()
    {
        using var line = FirstLine();
        using var flipped = OcrUtils.BitmapRotateClockWise180(line);

        var upright = models.Classifier.GetAngle(line, preserveAspectRatio: true);
        var turned = models.Classifier.GetAngle(flipped, preserveAspectRatio: true);

        Assert.Equal(0, upright.Index);
        Assert.Equal(1, turned.Index);
        Assert.True(turned.Score >= 0.9f);
    }

    [Fact]
    public void TheClassifierCanBeSkippedOrOutvoted()
    {
        using var line = FirstLine();
        using var flipped = OcrUtils.BitmapRotateClockWise180(line);

        Assert.All(models.Classifier.GetAngles([line, flipped], doAngle: false, mostAngle: false), a => Assert.Equal(-1, a.Index));
        Assert.All(models.Classifier.GetAngles([flipped, flipped, line], doAngle: true, mostAngle: true, true), a => Assert.Equal(1, a.Index));
        Assert.All(models.Classifier.GetAngles([line, line, flipped], doAngle: true, mostAngle: true, true), a => Assert.Equal(0, a.Index));
    }

    [Fact]
    public void TheRecogniserReadsALineWithAColumnPerCharacter()
    {
        using var line = FirstLine();

        var read = models.Recognizer.GetTextLine(line);

        AssertReads(OcrModels.Lines[0], string.Concat(read.Chars!));
        Assert.Equal(read.Chars!.Length, read.CharScores!.Length);
        Assert.Equal(read.Chars.Length, read.CharCols!.Length);
        Assert.True(read.CharCols.Zip(read.CharCols.Skip(1)).All(p => p.First < p.Second));
        Assert.InRange(read.CharCols[^1], 0, read.ColCount - 1);
        Assert.Single(models.Recognizer.GetTextLines([line]));
    }

    SKBitmap FirstLine() =>
        OcrUtils.GetRotateCropImage(models.Page, [new(10, 20), new(400, 20), new(400, 75), new(10, 75)]);

    static (int X0, int Y0, int X1, int Y1) Ink(SKBitmap page, int top, int bottom)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        for (var y = top; y < bottom; y++)
            for (var x = 0; x < page.Width; x++)
                if (page.GetPixel(x, y).Red < 128)
                    (x0, y0, x1, y1) = (Math.Min(x0, x), Math.Min(y0, y), Math.Max(x1, x), Math.Max(y1, y));
        return (x0, y0, x1, y1);
    }
}

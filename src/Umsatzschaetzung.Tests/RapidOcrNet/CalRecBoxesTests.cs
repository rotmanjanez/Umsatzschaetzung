using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

public class CalRecBoxesTests
{
    // A 400 x 48 crop read in 40 columns of 10 px: "ab cd" with its glyphs at columns 2, 4, 12, 14.
    static TextLine Line(params string[] chars) => new()
    {
        Chars = chars,
        CharScores = new[] { 0.9f, 0.7f, 0.8f, 0.6f, 1f }[..chars.Length],
        CharCols = new[] { 2, 4, 8, 12, 14 }[..chars.Length],
        ColCount = 40,
        LineTxtLen = 40,
    };

    static readonly CropContext Upright = new(100, 200, 400, 48, SKMatrix.Identity, false, false);

    static (int X0, int Y0, int X1, int Y1) Bounds(WordBox w) => Images.Bounds(w.BoxPoints);

    [Fact]
    public void WordsAreBoxedFromTheColumnsOfTheirGlyphs()
    {
        var words = CalRecBoxes.Build(Line("a", "b", " ", "c", "d"), Upright, cls180: false, returnSingleCharBox: false);

        Assert.NotNull(words);
        Assert.Equal(["ab", "cd"], words.Select(w => w.Text));
        Assert.Equal([(115, 200, 155, 248), (215, 200, 255, 248)], words.Select(Bounds));
        Assert.Equal(0.8f, words[0].Score, 1e-6f);
        Assert.Equal(0.8f, words[1].Score, 1e-6f);
        Assert.All(words, w => Assert.Equal(Images.Bounds(w.BoxPoints), (w.BoxPoints[0].X, w.BoxPoints[0].Y, w.BoxPoints[2].X, w.BoxPoints[2].Y)));
    }

    [Fact]
    public void AWideColumnGapSplitsAWordEvenWithoutASpace()
    {
        var line = new TextLine { Chars = ["a", "b", "c"], CharScores = [1f, 1f, 1f], CharCols = [2, 4, 20], ColCount = 40, LineTxtLen = 40 };
        var words = CalRecBoxes.Build(line, Upright, false, false);
        Assert.Equal(["ab", "c"], words!.Select(w => w.Text));
    }

    [Fact]
    public void AFlippedCropMirrorsTheWordsBackIntoPlace()
    {
        var words = CalRecBoxes.Build(Line("a", "b", " ", "c", "d"), Upright, cls180: true, returnSingleCharBox: false);

        Assert.NotNull(words);
        var ab = words.Single(w => w.Text == "ab");
        var cd = words.Single(w => w.Text == "cd");
        Assert.Equal((445, 200, 485, 248), Bounds(ab));
        Assert.Equal((345, 200, 385, 248), Bounds(cd));
        Assert.True(Bounds(cd).X1 < Bounds(ab).X0);
    }

    [Fact]
    public void SingleCharacterBoxesSplitEveryWord()
    {
        var chars = CalRecBoxes.Build(Line("a", "b", " ", "c", "d"), Upright, cls180: false, returnSingleCharBox: true);

        Assert.NotNull(chars);
        Assert.Equal(["a", "b", "c", "d"], chars.Select(c => c.Text));
        Assert.Equal([(115, 135), (135, 155), (215, 235), (235, 255)], chars.Select(c => (Bounds(c).X0, Bounds(c).X1)));
        Assert.Equal([0.9f, 0.7f, 0.6f, 1f], chars.Select(c => c.Score));
    }

    [Fact]
    public void ChineseGlyphsAreBoxedOneByOne()
    {
        var line = new TextLine { Chars = ["中", "文"], CharScores = [1f, 1f], CharCols = [2, 4], ColCount = 40, LineTxtLen = 40 };
        var boxes = CalRecBoxes.Build(line, Upright, false, false);
        Assert.Equal(["中", "文"], boxes!.Select(b => b.Text));
    }

    [Fact]
    public void AQuarterTurnedCropMapsItsWordsBackOntoTheTallStrip()
    {
        var tall = new CropContext(100, 200, 48, 400, SKMatrix.Identity, false, true);

        var words = CalRecBoxes.Build(Line("a", "b", " ", "c", "d"), tall, false, false);

        Assert.NotNull(words);
        Assert.Equal((100, 545, 148, 585), Bounds(words.Single(w => w.Text == "ab")));
        Assert.Equal((100, 445, 148, 485), Bounds(words.Single(w => w.Text == "cd")));
    }

    [Fact]
    public void AnEmptyLineHasNoWords()
    {
        Assert.Null(CalRecBoxes.Build(new TextLine { Chars = [], CharScores = [], CharCols = [], ColCount = 40 }, Upright, false, false));
        Assert.Null(CalRecBoxes.Build(new TextLine(), Upright, false, false));
        Assert.Null(CalRecBoxes.Build(new TextLine { Chars = [" "], CharScores = [1f], CharCols = [3], ColCount = 40 }, Upright, false, false));
    }
}

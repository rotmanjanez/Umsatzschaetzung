using RapidOcrNet;
using SkiaSharp;
using Ocr = Umsatzschaetzung.Service.RapidOcr;

namespace Umsatzschaetzung.Tests.Service;

public class OcrWordsTests
{
    static readonly SKPointI[] Box = [new(593, 1391), new(659, 1391), new(659, 1448), new(593, 1448)];

    static TextBlock Block(string text, int angle, params float[] scores) => new()
    {
        Text = text,
        BoxPoints = Box,
        AngleIndex = angle,
        Chars = [.. text.Select(c => c.ToString()).Take(scores.Length)],
        CharScores = scores,
        WordResults = [new WordBox { Text = text, BoxPoints = Box, Score = scores.DefaultIfEmpty().Average() }],
    };

    static List<string> Words(params TextBlock[] blocks) =>
        [.. Ocr.Words(new OcrResult { TextBlocks = blocks, StrRes = "" }).Select(w => w.Text)];

    [Fact]
    public void AnUprightCropKeepsItsWords() =>
        Assert.Equal(["Sack"], Words(Block("Sack", 0, 1f, 1f, 1f, 1f)));

    // R-25-052: the classifier called "kg" upside down, the recogniser read it at 1.00.
    [Fact]
    public void ACropCalledUpsideDownThatReadsCleanlyKeepsItsWords() =>
        Assert.Equal(["kg"], Words(Block("kg", 1, 1f, 1f)));

    [Fact]
    public void ACropTrulyOnItsHeadReadsAsGarbageAndIsDropped() =>
        Assert.Empty(Words(Block("L66", 1, 0.3f, 0.2f, 0.4f)));

    [Fact]
    public void ACropCalledUpsideDownThatReadNothingIsDropped() =>
        Assert.Empty(Words(Block("x", 1)));
}

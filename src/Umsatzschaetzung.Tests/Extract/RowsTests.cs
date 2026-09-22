using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Extract;

public class RowsTests
{
    static OcrWord W(string text, int x, int y, int h = 20) => new() { Text = text, Box = new Box(x, y, 40, h) };

    static List<string> Texts(IEnumerable<OcrWord> words) => [.. Rows.GroupRows(words).Select(r => string.Join(" ", r.Words.Select(w => w.Text)))];

    [Fact]
    public void WordsOnOnePrintedLineAreOneRowReadLeftToRight() =>
        Assert.Equal(["2 kg Tomaten 7,00"], Texts([W("Tomaten", 200, 100), W("2", 0, 100), W("7,00", 400, 100), W("kg", 100, 100)]));

    [Fact]
    public void AWordJitteringOffTheLineStaysOnIt() =>
        Assert.Equal(["a b c"], Texts([W("a", 0, 100), W("b", 100, 106), W("c", 200, 96)]));

    [Fact]
    public void RowsComeTopToBottomWhateverTheInputOrder() =>
        Assert.Equal(["Summe 7,00", "Tomaten 3,50", "Gurken 1,00"],
            Texts([W("Gurken", 0, 160), W("3,50", 200, 130), W("Summe", 0, 100), W("Tomaten", 0, 130), W("1,00", 200, 160), W("7,00", 200, 100)]));

    [Fact]
    public void AWordMoreThanHalfALineOffStartsARowOfItsOwn() =>
        Assert.Equal(["a", "b"], Texts([W("a", 0, 100), W("b", 100, 111)]));

    [Fact]
    public void TightlySetLinesStaySeparate() =>
        Assert.Equal(["a b", "c d"], Texts([W("a", 0, 100), W("b", 100, 100), W("c", 0, 120), W("d", 100, 120)]));

    // Anchored on the row's first word, so a skewed line cannot pull the next line in through its own drift.
    [Fact]
    public void ARowDoesNotChainThroughItsDrift() =>
        Assert.Equal(["a b", "c d"], Texts([W("a", 0, 100), W("b", 100, 108), W("c", 200, 116), W("d", 300, 124)]));

    [Fact]
    public void ATallWordJoinsTheLineItsCentreIsOn() =>
        Assert.Equal(["a B c"], Texts([W("a", 0, 100), W("B", 100, 90, 40), W("c", 200, 100)]));

    [Fact]
    public void BlankWordsAreDropped() =>
        Assert.Equal(["a"], Texts([W(" ", 0, 50), W("a", 0, 100), W("", 100, 100)]));

    [Fact]
    public void NoWordsAreNoRows() => Assert.Empty(Rows.GroupRows([]));

    [Fact]
    public void ARowsBoxCoversAllItsWords()
    {
        var row = Assert.Single(Rows.GroupRows([W("a", 0, 100), W("b", 100, 104)]));
        Assert.Equal(new Box(0, 100, 140, 24), row.Box);
    }

    [Fact]
    public void AUnionWithAnEmptyBoxIsTheOtherBox()
    {
        Assert.Equal(new Box(5, 6, 7, 8), Rows.Union(new Box(0, 0, 0, 0), new Box(5, 6, 7, 8)));
        Assert.Equal(new Box(0, 0, 30, 40), Rows.Union(new Box(0, 0, 10, 10), new Box(20, 30, 10, 10)));
    }
}

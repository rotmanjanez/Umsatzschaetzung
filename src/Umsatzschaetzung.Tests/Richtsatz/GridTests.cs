using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

public class GridTests
{
    static Word W(string text, double x0, double baseline, double width = 20, double size = 8) =>
        new(text, x0, x0 + width, baseline - size, baseline + 2, baseline, size);

    static Rule V(double x, double top, double bottom) => new(x - 0.25, x + 0.25, top, bottom);

    static Rule H(double y, double x0, double x1) => new(x0, x1, y - 0.25, y + 0.25);

    static Sheet Landscape(List<Word> words, List<Rule>? rules = null) => new(1, 842, 595, words, rules ?? []);

    [Fact]
    public void APortraitSheetIsOneHalf()
    {
        var sheet = new Sheet(1, 595, 842, [W("links", 50, 100), W("rechts", 400, 100)], []);

        var half = Assert.Single(Grid.Halves(sheet));
        Assert.Same(sheet.Words, half.Words);
    }

    [Fact]
    public void ALandscapeSheetSplitsAtTheGutterBetweenItsTwoPages()
    {
        var sheet = Landscape(
            [W("a", 50, 100, 350), W("b", 60, 120, 340), W("c", 440, 100, 350), W("d", 450, 120, 300)],
            [V(100, 50, 300), V(500, 50, 300), H(580, 20, 820)]);

        var halves = Grid.Halves(sheet).ToList();

        Assert.Equal(2, halves.Count);
        Assert.Equal(["a", "b"], halves[0].Words.Select(w => w.Text));
        Assert.Equal(["c", "d"], halves[1].Words.Select(w => w.Text));
        Assert.Equal([V(100, 50, 300)], halves[0].Rules);
        Assert.Equal([V(500, 50, 300)], halves[1].Rules);
    }

    [Fact]
    public void AGutterFarFromTheMiddleIsAColumnGapNotAPageBreak()
    {
        var sheet = Landscape([W("a", 50, 100, 100), W("b", 250, 100, 550)]);

        Assert.Single(Grid.Halves(sheet));
    }

    [Fact]
    public void AGapNarrowerThanTheGutterDoesNotSplit()
    {
        var sheet = Landscape([W("a", 50, 100, 365), W("b", 420, 100, 350)]);

        Assert.Single(Grid.Halves(sheet));
    }

    [Fact]
    public void AColumnBorderInTheMiddleOfAGapKeepsTheSheetWhole()
    {
        List<Word> words = [W("a", 50, 100, 363), W("c", 427, 100, 350)];

        Assert.Equal(2, Grid.Halves(Landscape(words)).Count());
        Assert.Single(Grid.Halves(Landscape(words, [V(420, 50, 300)])));
    }

    [Fact]
    public void LinesGroupWordsByBaselineAndReadThemLeftToRight()
    {
        var lines = Grid.Lines([W("zwei", 80, 101.9), W("eins", 10, 100), W("drei", 10, 103)]);

        Assert.Equal(["eins zwei", "drei"], lines.Select(Grid.Text));
    }

    [Fact]
    public void AWordSetSlightlyLowerStaysOnItsLine()
    {
        var lines = Grid.Lines([W("300.000", 50, 490.56), W("€", 80, 490.56), W("bis", 10, 492.12)]);

        Assert.Equal("bis 300.000 €", Grid.Text(Assert.Single(lines)));
    }

    [Fact]
    public void BlockJoinsTheLinesOfACell() =>
        Assert.Equal("Bau- und Heimwerkerbedarf", Grid.Block([W("Heimwerkerbedarf", 10, 110), W("Bau-", 10, 100), W("und", 40, 100)]));

    [Theory]
    [InlineData("Fleischerei und|Metzgerei", "Fleischerei und Metzgerei")]
    [InlineData("Nahrungs-|mittel", "Nahrungsmittel")]
    [InlineData("Gast|stätten", "Gaststätten")]
    [InlineData("Halb-|und Vollpension", "Halb- und Vollpension")]
    [InlineData("Nahrungs- und Genussmittel|versch. Art", "Nahrungs- und Genussmittel versch. Art")]
    [InlineData("Textilwaren,|einschl. Zubehör", "Textilwaren, einschl. Zubehör")]
    [InlineData("Bau-|Heimwerker", "Bau- Heimwerker")]
    [InlineData("Kfz 2|rad", "Kfz 2 rad")]
    [InlineData("(auch|mit Einzelhandel)", "(auch mit Einzelhandel)")]
    public void JoinMendsWordsBrokenAcrossLines(string lines, string joined) =>
        Assert.Equal(joined, Grid.Join(lines.Split('|')));

    [Fact]
    public void JoinOfNothingIsEmpty() => Assert.Equal("", Grid.Join([]));

    [Fact]
    public void VerticalsClusterTheSegmentsOfOneBorder()
    {
        var clusters = Grid.Verticals([V(100, 0, 10), V(100, 10, 20), V(101, 20, 50), V(200, 0, 10), H(60, 0, 300), V(300, 0, 3)]);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(50, clusters[0].Length, 3);
        Assert.Equal(100.6, clusters[0].Pos, 3);
        Assert.Equal((200.0, 10.0), clusters[1]);
    }

    [Fact]
    public void HorizontalsClusterFlatRulesByTheirHeight()
    {
        var clusters = Grid.Horizontals([H(60, 0, 100), H(61, 100, 300), H(90, 0, 50), V(10, 0, 100)]);

        Assert.Equal(2, clusters.Count);
        Assert.Equal(300, clusters[0].Length, 3);
        Assert.Equal((90.0, 50.0), clusters[1]);
    }

    [Fact]
    public void LongKeepsTheClustersNearTheLongest()
    {
        Assert.Equal([10.0, 30.0], Grid.Long([(10, 100), (20, 49), (30, 50)], 0.5));
        Assert.Empty(Grid.Long([], 0.5));
    }

    [Fact]
    public void AroundFindsTheBordersEitherSideOfEachColumnHeading() =>
        Assert.Equal([10.0, 50.0, 90.0], Grid.Around([10, 50, 90, 200], [30, 70], 0, 300));

    [Fact]
    public void AroundClosesAMissingOuterBorderAtThePageEdge()
    {
        Assert.Equal([10.0, 50.0, 95.0], Grid.Around([10, 50], [30, 70], 5, 95));
        Assert.Equal([5.0, 50.0, 90.0], Grid.Around([50, 90], [30, 70], 5, 95));
    }

    [Fact]
    public void AroundGivesUpWhereTwoHeadingsShareAColumn()
    {
        Assert.Null(Grid.Around([10, 90], [30, 70], 0, 100));
        Assert.Null(Grid.Around([10, 50, 130], [30, 70, 110], 0, 140));
    }

    [Fact]
    public void EdgesAreRowBreaksOfTheBordersAndRulesAcrossTheTable()
    {
        List<Rule> rules =
        [
            V(10, 100, 200), V(50, 100, 200), V(90, 100, 200),
            V(10, 200, 300), V(50, 200, 300), V(90, 200, 300),
            H(100, 10, 90), H(300, 10, 90), H(250, 10, 50),
        ];

        Assert.Equal([100.0, 200.0, 300.0], Grid.Edges(rules, 50));
        Assert.Equal([200.0, 300.0], Grid.Edges(rules, 150));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9.99, 0)]
    [InlineData(10, 1)]
    [InlineData(19.99, 1)]
    [InlineData(20, -1)]
    [InlineData(-0.01, -1)]
    public void SlotIsTheHalfOpenIntervalAPositionFallsIn(double pos, int slot) =>
        Assert.Equal(slot, Grid.Slot([0, 10, 20], pos));
}

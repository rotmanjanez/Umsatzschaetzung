using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

public class SheetsTests
{
    [Fact]
    public void BytesThatAreNoPdfAreRefused()
    {
        var e = Assert.Throws<InvalidDataException>(() => Sheets.Read([1, 2, 3]));
        Assert.Equal("pdf: nicht lesbar", e.Message);
    }

    [Fact]
    public void ALandscapeYearGivesOneSheetPerPageWithBothPrintedPagesOnIt()
    {
        var sheets = Sheets.Read(Pdfs.Bytes(2025));

        Assert.Equal(20, sheets.Count);
        Assert.Equal(Enumerable.Range(1, 20), sheets.Select(s => s.Number));
        Assert.All(sheets, s => Assert.InRange(s.Width / s.Height, 1.41, 1.42));
        Assert.Contains(sheets, s => Grid.Halves(s).Count() == 2);
    }

    [Fact]
    public void ABookletYearKeepsOnlyWhatItsCropBoxShows()
    {
        var sheets = Sheets.Read(Pdfs.Bytes(2022));

        Assert.Equal(44, sheets.Count);
        var sheet = sheets[4];
        Assert.Equal(421.58, sheet.Width, 2);
        Assert.True(sheet.Width < sheet.Height);
        Assert.Single(Grid.Halves(sheet));
        Assert.All(sheets, s =>
        {
            Assert.All(s.Words, w => Assert.InRange(w.Xc, 0, s.Width));
            Assert.All(s.Rules, r => Assert.InRange((r.X0 + r.X1) / 2, 0, s.Width));
        });
    }

    [Fact]
    public void WordsCarryTheirGeometryWithYGrowingDownwards()
    {
        var sheets = Sheets.Read(Pdfs.Bytes(2025));
        var head = sheets.SelectMany(s => Grid.Lines(s.Words)).First(l => Grid.Text(l).Contains("1 2 3 4 5 6 7 8"));

        Assert.All(head, w =>
        {
            Assert.True(w.X0 < w.X1);
            Assert.True(w.Top < w.Bottom);
            Assert.InRange(w.Baseline, w.Top, w.Bottom);
            Assert.True(w.Size > 0);
        });
        Assert.True(head.Zip(head.Skip(1)).All(p => p.First.X1 <= p.Second.X0));
    }

    [Fact]
    public void TableBordersComeBackAsRules()
    {
        var sheet = Sheets.Read(Pdfs.Bytes(2025))[10];

        Assert.NotEmpty(Grid.Verticals(sheet.Rules));
        Assert.NotEmpty(Grid.Horizontals(sheet.Rules));
        Assert.All(sheet.Rules, r => Assert.True(r.Width >= 0 && r.Height >= 0));
    }

    [Fact]
    public void AWordsCentreIsTheMiddleOfItsInk() =>
        Assert.Equal(15, new Word("x", 10, 20, 0, 5, 4, 8).Xc);

    [Fact]
    public void ARuleMeasuresItsBox()
    {
        var rule = new Rule(10, 12.5, 100, 140);

        Assert.Equal(2.5, rule.Width);
        Assert.Equal(40, rule.Height);
    }
}

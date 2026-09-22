using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Tagging;

public sealed class TaggerFixture : IDisposable
{
    public Tagger Tagger { get; } = new();

    public void Dispose() => Tagger.Dispose();
}

public class TaggerTests(TaggerFixture f) : IClassFixture<TaggerFixture>
{
    const int Width = 1240;
    const int Height = 1754;
    const int ItemRow = 3;

    static OcrWord W(string text, int x, int y, int w) => new() { Text = text, Box = new Box(x, y, w, 22) };

    // An invoice page at 150 dpi, in reading order: supplier, number and date, the item
    // table with two lines, the totals.
    static List<OcrWord> Page() =>
    [
        W("Rheinland", 80, 100, 150), W("Getränke", 240, 100, 130), W("GmbH", 380, 100, 70),
        W("Rechnung", 80, 160, 130), W("Nr.", 220, 160, 40), W("2024-04711", 270, 160, 150), W("Datum", 800, 160, 80), W("01.03.2024", 890, 160, 140),
        W("Pos", 80, 400, 50), W("Bezeichnung", 150, 400, 180), W("Menge", 600, 400, 90), W("Einheit", 700, 400, 90), W("Preis", 850, 400, 80), W("Betrag", 1000, 400, 100),
        W("1", 80, 440, 15), W("Pils", 150, 440, 60), W("Fass", 220, 440, 60), W("50", 290, 440, 30), W("l", 325, 440, 10), W("12", 600, 440, 30), W("Keg", 700, 440, 50), W("92,50", 850, 440, 80), W("1.110,00", 1000, 440, 110),
        W("2", 80, 480, 15), W("Doppelkorn", 150, 480, 150), W("0,7", 310, 480, 40), W("l", 355, 480, 10), W("6", 600, 480, 15), W("Fl", 700, 480, 25), W("8,90", 850, 480, 60), W("53,40", 1000, 480, 80),
        W("Summe", 700, 700, 90), W("netto", 800, 700, 70), W("1.163,40", 1000, 700, 110),
        W("USt", 700, 740, 50), W("19%", 770, 740, 50), W("221,05", 1000, 740, 90),
        W("Gesamt", 700, 780, 90), W("1.384,45", 1000, 780, 110),
    ];

    static int[] RowStarts => [0, 3, 8, 14, 23, 31, 34, 37, 39];

    static string Dump(List<TaggedWord> t) =>
        string.Join('\n', t.Select(x => $"{x.Row} {x.Word.Text} {x.Field} {x.Role} {x.Conf:F2} col {x.Col} {(x.CellStart ? "start" : "")}"));

    [Fact]
    public void EveryWordComesBackOnceInReadingOrderOnItsRow()
    {
        var page = Page();
        var tagged = f.Tagger.Tag(page, Width, Height);
        Assert.Equal(page, tagged.Select(t => t.Word));
        var rows = RowStarts;
        for (var r = 0; r < rows.Length - 1; r++)
            for (var i = rows[r]; i < rows[r + 1]; i++)
                Assert.Equal(r, tagged[i].Row);
    }

    [Fact]
    public void ConfidenceIsAProbability() =>
        Assert.All(f.Tagger.Tag(Page(), Width, Height), t => Assert.InRange(t.Conf, 0f, 1f));

    [Fact]
    public void AnObviousItemRowIsReadAsALineItemOfTableCells()
    {
        var tagged = f.Tagger.Tag(Page(), Width, Height);
        var item = tagged.FindAll(t => t.Row == ItemRow);
        Assert.True(item.TrueForAll(t => t.Role == Role.LineItem), Dump(tagged));
        Assert.True(item.TrueForAll(t => t.Field == Field.Cell), Dump(tagged));
        Assert.True(item[0].CellStart, Dump(tagged));
        Assert.True(item.Select(t => t.Col).Distinct().Count() >= 4, Dump(tagged));
        Assert.Equal(item.Select(t => t.Col).Order(), item.Select(t => t.Col));
    }

    [Fact]
    public void TheHeaderAndTheTotalsAreTaggedAsSuch()
    {
        var tagged = f.Tagger.Tag(Page(), Width, Height);
        Assert.True(tagged.Find(t => t.Word.Text == "2024-04711")?.Field == Field.InvoiceNumber, Dump(tagged));
        Assert.True(tagged.Find(t => t.Word.Text == "01.03.2024")?.Field == Field.InvoiceDate, Dump(tagged));
        Assert.True(tagged.Find(t => t.Word.Text == "1.384,45")?.Field == Field.GrossTotal, Dump(tagged));
        Assert.True(tagged.Find(t => t.Word.Text == "Bezeichnung")?.Role == Role.ColumnHeader, Dump(tagged));
        Assert.True(tagged.Find(t => t.Word.Text == "Gesamt")?.Role == Role.Total, Dump(tagged));
    }

    [Fact]
    public void AnEmptyPageHasNoWords() => Assert.Empty(f.Tagger.Tag(new List<OcrWord>(), Width, Height));

    [Fact]
    public void ABlankWordIsNoWord()
    {
        var page = Page();
        page.Insert(5, W("  ", 250, 160, 10));
        page.Add(W("", 80, 1500, 10));
        Assert.Equal(Page().Select(w => w.Text), f.Tagger.Tag(page, Width, Height).Select(t => t.Word.Text));
    }

    [Fact]
    public void OneWordIsOneTaggedWord()
    {
        var word = W("Rechnung", 80, 160, 130);
        var tagged = Assert.Single(f.Tagger.Tag([word], Width, Height));
        Assert.Same(word, tagged.Word);
        Assert.Equal(0, tagged.Row);
        Assert.InRange(tagged.Conf, 0f, 1f);
    }

    [Fact]
    public void WordsOutOfOrderAreTaggedAsIfRead()
    {
        var page = Page();
        var shuffled = page.OrderBy(w => w.Text.GetHashCode() ^ w.Box.X).ToList();
        Assert.Equal(Key(f.Tagger.Tag(page, Width, Height)), Key(f.Tagger.Tag(shuffled, Width, Height)));
    }

    // Boxes are binned against the page size, so the resolution of the scan does not matter.
    [Fact]
    public void TheSamePageAtTwiceTheResolutionIsTaggedTheSame()
    {
        var sharp = Page().Select(w => new OcrWord { Text = w.Text, Box = new Box(w.Box.X * 2, w.Box.Y * 2, w.Box.W * 2, w.Box.H * 2) }).ToList();
        var a = f.Tagger.Tag(Page(), Width, Height);
        var b = f.Tagger.Tag(sharp, Width * 2, Height * 2);
        Assert.Equal(Key(a), Key(b));
        Assert.Equal(a.Select(t => t.Conf), b.Select(t => t.Conf));
    }

    [Fact]
    public void TheSameBoxesOnALargerPageAreReadDifferently()
    {
        var a = f.Tagger.Tag(Page(), Width, Height);
        var b = f.Tagger.Tag(Page(), Width * 4, Height * 4);
        Assert.NotEqual(a.Select(t => t.Conf), b.Select(t => t.Conf));
    }

    [Fact]
    public void BoxesOutsideThePageAreClampedNotRejected()
    {
        var page = Page();
        page.Add(W("Seite", 5000, 1900, 400));
        var tagged = f.Tagger.Tag(page, Width, Height);
        Assert.Equal(page.Count, tagged.Count);
        Assert.Equal(page.Count, f.Tagger.Tag(page, 0, 0).Count);
    }

    [Fact]
    public void RowsGivenAreKept()
    {
        var ordered = Page().Select((w, i) => (w, i % 3)).ToList();
        var tagged = f.Tagger.Tag(ordered, Width, Height);
        Assert.Equal(ordered.Select(o => (o.w, o.Item2)), tagged.Select(t => (t.Word, t.Row)));
    }

    // Longer than one window and with more rows than the role head pools: the windows
    // overlap and every word still comes back once.
    [Fact]
    public void APageLongerThanOneWindowKeepsEveryWord()
    {
        var page = new List<OcrWord>();
        for (var r = 0; r < 160; r++)
        {
            var y = 20 + r * 30;
            page.Add(W((r + 1).ToString(), 80, y, 20));
            page.Add(W("Weizenmehl Type 550", 150, y, 300));
            page.Add(W($"{r + 3},{r % 100:00}", 1000, y, 80));
        }
        var tagged = f.Tagger.Tag(page, Width, 5000);
        Assert.Equal(page, tagged.Select(t => t.Word));
        Assert.Equal(Enumerable.Range(0, 160).SelectMany(r => new[] { r, r, r }), tagged.Select(t => t.Row));
        Assert.All(tagged, t => Assert.InRange(t.Conf, 0f, 1f));
    }

    static List<(string, Field?, Role, int, int, bool)> Key(List<TaggedWord> t) =>
        [.. t.Select(x => (x.Word.Text, x.Field, x.Role, x.Row, x.Col, x.CellStart))];
}

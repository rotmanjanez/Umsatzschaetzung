using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Extract;

public sealed class TaggerFixture : IDisposable
{
    public Tagger Tagger { get; } = new();

    public void Dispose() => Tagger.Dispose();
}

public class ExtractorTests(TaggerFixture fixture) : IClassFixture<TaggerFixture>
{
    // An A4 page at 150 dpi as OCR would hand it over: one box per word, 12 px a letter.
    static OcrPage Page(params (int X, int Y, string Text)[] lines)
    {
        var page = new OcrPage { Width = 1240, Height = 1754 };
        foreach (var (x0, y, text) in lines)
        {
            var x = x0;
            foreach (var word in text.Split(' '))
            {
                page.Words.Add(new OcrWord { Text = word, Box = new Box(x, y, word.Length * 12, 22), Confidence = 0.99f });
                x += word.Length * 12 + 10;
            }
        }
        return page;
    }

    static OcrPage Invoice(string net = "39,00", string first = "24,00") => Page(
        (100, 80, "Pucher OG"),
        (100, 120, "Fleischerei Hauptstraße 1 8010 Graz"),
        (100, 240, "Rechnung"),
        (100, 300, "Rechnungsnummer:"), (400, 300, "RE-2025-17"),
        (100, 330, "Rechnungsdatum:"), (400, 330, "08.11.2025"),
        (100, 420, "Pos"), (170, 420, "Menge"), (270, 420, "Einheit"), (370, 420, "Bezeichnung"), (800, 420, "Einzelpreis"), (1000, 420, "Gesamt"),
        (100, 460, "1"), (170, 460, "2"), (270, 460, "kg"), (370, 460, "Rindfleisch"), (800, 460, "12,00"), (1000, 460, first),
        (100, 490, "2"), (170, 490, "10"), (270, 490, "Stk"), (370, 490, "Bratwurst"), (800, 490, "1,50"), (1000, 490, "15,00"),
        (700, 560, "Nettobetrag"), (1000, 560, net),
        (700, 590, "zzgl. 10 % USt"), (1000, 590, "3,90"),
        (700, 620, "Gesamtbetrag"), (1000, 620, "42,90"));

    [Fact]
    public async Task AnOcrPageIsReadIntoAnInvoiceThatChecksClean()
    {
        var page = Invoice();
        var inv = await Extractor.InvoiceAsync(fixture.Tagger, [page], TestContext.Current.CancellationToken);

        Assert.Equal(
            [(1L, "Rindfleisch", 2000L, "KGM", 12_000_000L, 2400L, 1000L), (2L, "Bratwurst", 10_000L, "H87", 1_500_000L, 1500L, 1000L)],
            inv.Lines.Select(l => (l.No, l.Name, l.Quantity, l.UnitCode, l.UnitPrice, l.LineNet, l.Vat)));
        Assert.Equal("RE-2025-17", inv.Number);
        Assert.Equal(new DateOnly(2025, 11, 8), inv.Date);
        Assert.Equal((3900L, 4290L), (inv.StatedNet, inv.StatedGross));
        Assert.Equal(inv.Lines, page.Lines.Select(l => l.Parsed));
        Assert.Empty(page.Flags);
        Assert.All(page.Lines, l => Assert.Empty(l.Flags));
    }

    [Fact]
    public async Task ALineFlagLandsOnItsLineAndAnInvoiceFlagOnTheFirstPage()
    {
        var page = Invoice(net: "41,00", first: "25,00");
        await Extractor.InvoiceAsync(fixture.Tagger, [page], TestContext.Current.CancellationToken);

        Assert.Equal(["line_total"], page.Lines[0].Flags.Select(f => f.Code));
        Assert.Empty(page.Lines[1].Flags);
        Assert.Contains(page.Flags, f => f.Code == "sum_net" && f.LineNo == 0);
        Assert.DoesNotContain(page.Flags, f => f.LineNo != 0);
    }

    [Fact]
    public async Task ACancelledReadNeverTouchesTheModel()
    {
        using var unused = new Tagger();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Extractor.InvoiceAsync(unused, [Invoice()], new CancellationToken(canceled: true)));
    }

    [Fact]
    public async Task NoPagesIsAnEmptyInvoice()
    {
        var inv = await Extractor.InvoiceAsync(fixture.Tagger, [], TestContext.Current.CancellationToken);
        Assert.Empty(inv.Lines);
        Assert.Equal(Source.Scan, inv.Source);
    }
}

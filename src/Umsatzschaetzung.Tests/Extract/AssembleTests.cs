using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Extract;

public class AssembleTests
{
    static Sheet Head(Sheet sheet) => sheet
        .Line(Role.Header, (50, "Pucher OG", Field.Supplier))
        .Line(Role.Header, (50, "Rechnungsnummer", Field.NumberLabel), (250, "RE-2025-17", Field.InvoiceNumber))
        .Line(Role.Header, (50, "Datum", Field.DateLabel), (250, "08.11.2025", Field.InvoiceDate));

    static Sheet Columns(Sheet sheet) => sheet
        .Cells(Role.ColumnHeader, (100, "Menge"), (200, "Einheit"), (300, "Bezeichnung"), (700, "Einzelpreis"), (900, "Gesamt"));

    static Sheet Totals(Sheet sheet, string net = "11,00", string gross = "11,77") => sheet
        .Line(Role.Total, (50, "Netto", Field.NetLabel), (900, net, Field.NetTotal))
        .Line(Role.Total, (50, "MwSt", Field.VatLabel), (300, "7 %", Field.Vat), (900, "0,77", null))
        .Line(Role.Total, (50, "Brutto", Field.GrossLabel), (900, gross, Field.GrossTotal));

    static Sheet Receipt() => Totals(Columns(Head(new Sheet()))
        .Cells(Role.LineItem, (100, "2"), (200, "kg"), (300, "Tomaten rot"), (700, "3,50"), (900, "7,00"))
        .Cells(Role.LineItem, (100, "10"), (200, "Stk"), (300, "Semmel"), (700, "0,40"), (900, "4,00")));

    static (Invoice Invoice, List<OcrPage> Pages) Read(params Sheet[] sheets) => Read([.. sheets.Select(s => s.Words)]);

    static (Invoice Invoice, List<OcrPage> Pages) Read(List<List<TaggedWord>> tagged)
    {
        var pages = tagged.Select(_ => new OcrPage()).ToList();
        return (Assemble.Invoice(tagged, pages), pages);
    }

    [Fact]
    public void ATaggedPageBecomesAnInvoiceThatChecksClean()
    {
        var (inv, pages) = Read(Receipt());
        Assert.Equal((Source.Scan, "EUR"), (inv.Source, inv.Currency));
        Assert.Equal("Pucher OG", inv.SupplierName);
        Assert.Equal("RE-2025-17", inv.Number);
        Assert.Equal(new DateOnly(2025, 11, 8), inv.Date);
        Assert.Equal((1100L, 1177L), (inv.StatedNet, inv.StatedGross));
        Assert.Equal(2, inv.Lines.Count);
        var l = inv.Lines[0];
        Assert.Equal((1L, "Tomaten rot", 2000L, "KGM", 3_500_000L, 1000L, 700L, 700L),
            (l.No, l.Name, l.Quantity, l.UnitCode, l.UnitPrice, l.PriceBaseQty, l.LineNet, l.Vat));
        Assert.Equal((2L, "H87", 10_000L, 400_000L, 400L), (inv.Lines[1].No, inv.Lines[1].UnitCode, inv.Lines[1].Quantity, inv.Lines[1].UnitPrice, inv.Lines[1].LineNet));

        var flags = Check.Invoice(inv);
        Assert.Empty(flags);
        Assert.True(Check.Complete(inv, flags));
    }

    [Fact]
    public void ThePageKeepsWhereEveryValueWasRead()
    {
        var (inv, pages) = Read(Receipt());
        var page = pages[0];
        Assert.Equal([inv.Lines[0], inv.Lines[1]], page.Lines.Select(l => l.Parsed));
        Assert.Equal("Tomaten rot", page.Lines[0].Cells[Field.Name].Text);
        Assert.Equal(
            [Field.Vat, Field.InvoiceNumber, Field.InvoiceDate, Field.Supplier, Field.NetTotal, Field.GrossTotal],
            page.Header.Keys.Order());
        Assert.Equal("Pucher OG", page.Header[Field.Supplier].Text);
        Assert.Equal(new Box(50, 0, 88, 20), page.Header[Field.Supplier].Box);
    }

    [Fact]
    public void ARowWithoutANumberIsANoteNotAPosition()
    {
        var (inv, _) = Read(Columns(new Sheet())
            .Cells(Role.LineItem, (300, "Lieferung vom 03.11."))
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,50"), (900, "7,00"))
            .Cells(Role.LineItem, (100, "1"), (700, "2,00"))
            .Cells(Role.LineItem, (100, "3"), (300, "Gurken"), (700, "1,00"), (900, "3,00")));
        Assert.Equal([(1L, "Tomaten"), (2L, "Gurken")], inv.Lines.Select(l => (l.No, l.Name)));
    }

    [Fact]
    public void AnItemWithANetButNoNameIsStillAPosition()
    {
        var (inv, _) = Read(Columns(new Sheet()).Cells(Role.LineItem, (100, "2"), (700, "3,50"), (900, "7,00")));
        Assert.Equal(700, Assert.Single(inv.Lines).LineNet);
    }

    [Fact]
    public void TheArticleIdIsTakenAsPrinted()
    {
        var (inv, _) = Read(new Sheet()
            .Cells(Role.ColumnHeader, (50, "Art.-Nr."), (300, "Bezeichnung"), (900, "Betrag"))
            .Cells(Role.LineItem, (50, "0815-3"), (300, "Tomaten"), (900, "7,00")));
        Assert.Equal("0815-3", inv.Lines[0].SellerArticleId);
        Assert.Null(Read(Receipt()).Invoice.Lines[0].SellerArticleId);
    }

    [Fact]
    public void AGroupedQuantityIsTakenBackByTheLineNet()
    {
        var (inv, _) = Read(Columns(new Sheet()).Cells(Role.LineItem, (100, "5.450"), (200, "kg"), (300, "Rind"), (700, "12,00"), (900, "65,40")));
        Assert.Equal(5450, inv.Lines[0].Quantity);
    }

    [Fact]
    public void LettersInNumericCellsAndAConfusedDigitArePutBack()
    {
        var (inv, _) = Read(Columns(new Sheet())
            .Cells(Role.LineItem, (100, "6"), (200, "kg"), (300, "Rind"), (700, "O,9O"), (900, "8,10")));
        Assert.Equal((9000L, 900_000L, 810L), (inv.Lines[0].Quantity, inv.Lines[0].UnitPrice, inv.Lines[0].LineNet));
    }

    [Fact]
    public void AUnitLetterReadAsADigitInTheNameComesBack()
    {
        var (inv, _) = Read(Columns(new Sheet()).Cells(Role.LineItem, (100, "1"), (200, "Kan"), (300, "Frittieröl 10 1"), (700, "20,00"), (900, "20,00")));
        Assert.Equal("Frittieröl 10 l", inv.Lines[0].Name);
    }

    [Fact]
    public void ALineWithoutARateTakesTheOneTheTotalsState()
    {
        var sheet = Columns(new Sheet()).Cells(Role.ColumnHeader, (1000, "MwSt"));
        var (inv, _) = Read(Totals(sheet
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,50"), (900, "7,00"), (1000, "19 %"))
            .Cells(Role.LineItem, (100, "1"), (300, "Gurken"), (700, "4,00"), (900, "4,00"))));
        Assert.Equal([1900L, 700L], inv.Lines.Select(l => l.Vat));
    }

    [Fact]
    public void TheTotalsRateKeepsWhereItWasStated()
    {
        var (_, pages) = Read(new Sheet(), Receipt());
        Assert.False(pages[0].Header.ContainsKey(Field.Vat));
        var stated = pages[1].Header[Field.Vat];
        Assert.Equal("7", stated.Text);
        Assert.Equal(50, stated.Box.X);
        Assert.True(stated.Box.X + stated.Box.W > 300);
    }

    [Fact]
    public void OfTwoStatedRatesTheOneOnTheVatLabelsRowCounts()
    {
        var (inv, _) = Read(Columns(new Sheet())
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,50"), (900, "7,00"))
            .Line(Role.Total, (50, "Skonto", Field.OtherLabel), (300, "2 %", Field.Vat))
            .Line(Role.Total, (50, "USt", Field.VatLabel), (300, "7 %", Field.Vat)));
        Assert.Equal(700, inv.Lines[0].Vat);
    }

    [Fact]
    public void TwoGenuinelyStatedRatesAreAttributedToNoLine()
    {
        var (inv, _) = Read(Columns(new Sheet())
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,50"), (900, "7,00"))
            .Line(Role.Total, (50, "USt", Field.VatLabel), (300, "7 %", Field.Vat))
            .Line(Role.Total, (50, "USt", Field.VatLabel), (300, "19 %", Field.Vat)));
        Assert.Equal(0, inv.Lines[0].Vat);
    }

    [Fact]
    public void ARateOutsideTheTotalsBlockIsNotTheTotalsRate()
    {
        var (inv, _) = Read(Columns(new Sheet())
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,50"), (900, "7,00"))
            .Line(Role.Footer, (50, "USt", Field.VatLabel), (300, "7 %", Field.Vat)));
        Assert.Equal(0, inv.Lines[0].Vat);
    }

    // "Kundennummer 48211" above "Rechnungsnummer RE250292/6" are two values, and the label decides.
    static Sheet TwoNumbers(bool labelled) => new Sheet()
        .Line(Role.Header, 0.95f, (50, "Kundennummer", Field.OtherLabel), (250, "48211", Field.InvoiceNumber))
        .Line(Role.Header, 0.6f, (50, "Rechnungsnummer", labelled ? Field.NumberLabel : Field.OtherLabel), (250, "RE250292/6", Field.InvoiceNumber));

    [Fact]
    public void ALabelClaimsTheValueBesideItOverAMoreConfidentOne() =>
        Assert.Equal("RE250292/6", Read(TwoNumbers(labelled: true)).Invoice.Number);

    [Fact]
    public void WithoutALabelTheMoreConfidentRunWins() =>
        Assert.Equal("48211", Read(TwoNumbers(labelled: false)).Invoice.Number);

    [Fact]
    public void ALabelClaimsTheValueDirectlyBeneathIt()
    {
        var (inv, _) = Read(new Sheet()
            .Line(Role.Header, 0.9f, (600, "Kunde 4711", Field.InvoiceNumber))
            .Line(Role.Header, (50, "Rechnung Nr.", Field.NumberLabel))
            .Line(Role.Header, 0.5f, (50, "RE-17", Field.InvoiceNumber)));
        Assert.Equal("RE-17", inv.Number);
    }

    [Fact]
    public void ARunInTheExpectedRowRoleBeatsAMoreConfidentOneElsewhere()
    {
        var (inv, _) = Read(new Sheet()
            .Line(Role.Footer, 0.99f, (600, "12,00", Field.NetTotal))
            .Line(Role.Total, 0.5f, (50, "11,00", Field.NetTotal)));
        Assert.Equal(1100, inv.StatedNet);
    }

    [Fact]
    public void TheLongestSupplierRunWinsAmongEquals()
    {
        var (inv, _) = Read(new Sheet()
            .Line(Role.Header, (50, "Pucher", Field.Supplier))
            .Line(Role.Header, (50, "Rechnung", null))
            .Line(Role.Header, (600, "Fleischerei Pucher OG", Field.Supplier)));
        Assert.Equal("Fleischerei Pucher OG", inv.SupplierName);
    }

    [Fact]
    public void ASupplierWrappedOverTwoLinesIsOneName() =>
        Assert.Equal("Bäckerei Huber Feinbäckerei GmbH", Read(new Sheet()
            .Line(Role.Header, (50, "Bäckerei Huber", Field.Supplier))
            .Line(Role.Header, (50, "Feinbäckerei GmbH", Field.Supplier))).Invoice.SupplierName);

    [Fact]
    public void AWordmarkOverTheSenderLineIsNotReadTwice() =>
        Assert.Equal("Pucher OG", Read(new Sheet()
            .Line(Role.Header, (50, "PUCHER", Field.Supplier))
            .Line(Role.Header, (50, "Pucher OG", Field.Supplier))).Invoice.SupplierName);

    [Theory]
    [InlineData("Pucher Pucher OG", "Pucher OG")]
    [InlineData("GROSSHANDEL Großhandel Maier", "Großhandel Maier")]
    [InlineData("Trautm ann Trautmann GmbH", "Trautmann GmbH")]
    public void ANamePrintedTwiceOnOneLineKeepsTheFullerPrinting(string printed, string name) =>
        Assert.Equal(name, Read(new Sheet().Line(Role.Header, (50, printed, Field.Supplier))).Invoice.SupplierName);

    [Fact]
    public void ADateCutInHalfByTheColumnGapIsRejoined()
    {
        var (inv, _) = Read(new Sheet()
            .Line(Role.Header, (50, "Datum", Field.DateLabel), (200, "19.", Field.InvoiceDate), (400, "Dezember 2025", Field.InvoiceDate)));
        Assert.Equal(new DateOnly(2025, 12, 19), inv.Date);
    }

    [Fact]
    public void TwoWholeDatesOnARowAreNotJoined()
    {
        var (inv, _) = Read(new Sheet()
            .Line(Role.Header, (50, "Datum", Field.DateLabel), (200, "08.11.2025", Field.InvoiceDate), (500, "09.11.2025", Field.InvoiceDate)));
        Assert.Equal(new DateOnly(2025, 11, 8), inv.Date);
    }

    [Fact]
    public void ALinePrintedAcrossTwoRowsBySkewReadsInPrintedOrder()
    {
        TaggedWord W(string text, int x, int y, int row) =>
            new(Sheet.Word(text, x, y), Field.InvoiceNumber, Role.Header, row);
        var (inv, _) = Read([[W("001612", 100, 34, 0), W("RG", 60, 40, 1)]]);
        Assert.Equal("RG 001612", inv.Number);
    }

    [Fact]
    public void TheFirstPageWithARunDecides()
    {
        var first = new Sheet().Line(Role.Header, 0.5f, (50, "Pucher OG", Field.Supplier));
        var third = new Sheet().Line(Role.Header, 0.99f, (50, "Pucher Fleischwaren OG", Field.Supplier));
        var (inv, pages) = Read(new Sheet(), first, third);
        Assert.Equal("Pucher OG", inv.SupplierName);
        Assert.Contains(Field.Supplier, pages[1].Header.Keys);
        Assert.DoesNotContain(Field.Supplier, pages[2].Header.Keys);
    }

    [Fact]
    public void ItemsOnALaterPageContinueTheColumnsAndTheNumbering()
    {
        var first = Columns(new Sheet()).Cells(Role.LineItem, (100, "2"), (200, "kg"), (300, "Tomaten"), (700, "3,50"), (900, "7,00"));
        var second = new Sheet().Cells(Role.LineItem, (100, "1"), (200, "Stk"), (300, "Gurken"), (700, "1,00"), (900, "9,99"));
        var (inv, pages) = Read(first, second);
        Assert.Equal([1L, 2L], inv.Lines.Select(l => l.No));
        Assert.Equal((1_000_000L, 999L), (inv.Lines[1].UnitPrice, inv.Lines[1].LineNet));
        Assert.Same(inv.Lines[1], Assert.Single(pages[1].Lines).Parsed);
    }

    [Theory]
    [InlineData("0,00")]
    [InlineData("Summe")]
    [InlineData("-5,00")]
    public void ATotalThatIsNoPositiveAmountIsNotStated(string printed)
    {
        var (inv, _) = Read(Totals(new Sheet(), net: printed, gross: printed));
        Assert.Equal((null, null), (inv.StatedNet, inv.StatedGross));
    }

    [Fact]
    public void AStatedTotalIsReadThroughDigitShapes() =>
        Assert.Equal(1100, Read(Totals(new Sheet(), net: "11,OO €")).Invoice.StatedNet);

    [Fact]
    public void NothingTaggedIsAnEmptyInvoice()
    {
        var (inv, pages) = Read(new Sheet());
        Assert.Empty(inv.Lines);
        Assert.Equal(("", "", null), (inv.SupplierName, inv.Number, inv.Date));
        Assert.Empty(pages[0].Header);
        Assert.Empty(Assemble.Invoice([], []).Lines);
    }
}

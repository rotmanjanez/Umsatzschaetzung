using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Extract;

public class TableTests
{
    static Sheet Headed() => new Sheet()
        .Cells(Role.ColumnHeader, (50, "Pos"), (100, "Menge"), (200, "Einheit"), (300, "Bezeichnung"), (700, "Einzelpreis"), (900, "Gesamt"));

    static List<Dictionary<Field, OcrWord>> Items(Sheet sheet, Table? previous = null) =>
        Table.Read(sheet.Words, previous).Items;

    static Dictionary<Field, string> Texts(Dictionary<Field, OcrWord> item) =>
        item.ToDictionary(kv => kv.Key, kv => kv.Value.Text);

    static void Row(Dictionary<Field, OcrWord> item, string? quantity, string? unit, string? name, string? price, string? net)
    {
        var t = Texts(item);
        Assert.Equal(quantity, t.GetValueOrDefault(Field.Quantity));
        Assert.Equal(unit, t.GetValueOrDefault(Field.Unit));
        Assert.Equal(name, t.GetValueOrDefault(Field.Name));
        Assert.Equal(price, t.GetValueOrDefault(Field.UnitPrice));
        Assert.Equal(net, t.GetValueOrDefault(Field.LineNet));
    }

    [Fact]
    public void TheHeaderNamesTheColumnsAndEveryLineItemRowIsOneItem()
    {
        var items = Items(Headed()
            .Cells(Role.LineItem, (50, "1"), (100, "2"), (200, "kg"), (300, "Tomaten rot"), (700, "3,50"), (900, "7,00"))
            .Cells(Role.LineItem, (50, "2"), (100, "10"), (200, "Stk"), (300, "Semmel"), (700, "0,40"), (900, "4,00")));
        Assert.Equal(2, items.Count);
        Row(items[0], "2", "kg", "Tomaten rot", "3,50", "7,00");
        Row(items[1], "10", "Stk", "Semmel", "0,40", "4,00");
        Assert.DoesNotContain(Field.ArticleId, items[0].Keys);
    }

    [Fact]
    public void AnItemKeepsTheBoxOfItsCell()
    {
        var item = Items(Headed().Cells(Role.LineItem, (100, "2"), (300, "Tomaten rot"), (700, "3,50"), (900, "7,00")))[0];
        Assert.Equal(new Box(300, 30, 108, 20), item[Field.Name].Box);
    }

    [Fact]
    public void WithoutAHeaderQuantityTimesPriceEqualsNetDecidesTheAmountColumns()
    {
        var items = Items(new Sheet()
            .Cells(Role.LineItem, (50, "1"), (100, "2"), (200, "kg"), (300, "Tomaten rot"), (700, "7,00"), (900, "3,50"))
            .Cells(Role.LineItem, (50, "2"), (100, "10"), (200, "Stk"), (300, "Semmel"), (700, "4,00"), (900, "0,40")));
        Row(items[0], "2", "kg", "Tomaten rot", "3,50", "7,00");
        Row(items[1], "10", "Stk", "Semmel", "0,40", "4,00");
    }

    [Fact]
    public void WithNoRowAddingUpTheAmountsReadAsPriceThenNet()
    {
        var items = Items(new Sheet().Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,00"), (900, "9,99")));
        Row(items[0], "2", null, "Tomaten", "3,00", "9,99");
    }

    [Fact]
    public void ARateColumnIsTheVat()
    {
        var items = Items(new Sheet().Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (500, "7 %"), (700, "3,50"), (900, "7,00")));
        Assert.Equal("7 %", items[0][Field.Vat].Text);
        Row(items[0], "2", null, "Tomaten", "3,50", "7,00");
    }

    [Fact]
    public void ALeftAlignedHeaderOverRightAlignedAmountsStillNamesThem()
    {
        var items = Items(new Sheet()
            .Cells(Role.ColumnHeader, (100, "Menge"), (300, "Bezeichnung"), (700, "Einzelpreis"), (850, "Gesamt"))
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "3,50"), (920, "9,99")));
        Row(items[0], "2", null, "Tomaten", "3,50", "9,99");
    }

    [Theory]
    [InlineData("Menge/Stk", Field.Quantity)]
    [InlineData("Merge", Field.Quantity)]
    [InlineData("Eirheit", Field.Unit)]
    [InlineData("ME", Field.Unit)]
    [InlineData("Art.-Nr.", Field.ArticleId)]
    [InlineData("Artikelnummer", Field.ArticleId)]
    [InlineData("MwSt", Field.Vat)]
    public void AHeadingIsReadExactlyByPrefixOrOneLetterOff(string heading, Field meaning)
    {
        var items = Items(new Sheet()
            .Cells(Role.ColumnHeader, (100, heading), (300, "Bezeichnung"))
            .Cells(Role.LineItem, (100, "x"), (300, "Tomaten")));
        Assert.Equal("x", items[0][meaning].Text);
    }

    [Fact]
    public void AColumnHeadedWithAnIgnoredKeyIsDropped()
    {
        var items = Items(new Sheet()
            .Cells(Role.ColumnHeader, (100, "Lieferdatum"), (300, "Bezeichnung"))
            .Cells(Role.LineItem, (100, "12.03."), (300, "Tomaten")));
        Assert.Equal([Field.Name], items[0].Keys);
    }

    [Fact]
    public void AnArticleHeadingOverNumericCodesIsTheArticleId()
    {
        var items = Items(new Sheet()
            .Cells(Role.ColumnHeader, (100, "Artikel"), (300, "Bezeichnung"), (900, "Betrag"))
            .Cells(Role.LineItem, (100, "10234"), (300, "Tomaten"), (900, "7,00"))
            .Cells(Role.LineItem, (100, "20456"), (300, "Gurken"), (900, "3,00")));
        Assert.Equal(["10234", "20456"], items.Select(i => i[Field.ArticleId].Text));
        Assert.Equal(["Tomaten", "Gurken"], items.Select(i => i[Field.Name].Text));
    }

    [Fact]
    public void ACodeColumnLeftOfTheNameIsTheArticleId()
    {
        var items = Items(new Sheet()
            .Cells(Role.LineItem, (100, "A-10234"), (300, "Tomaten rot"), (900, "7,00"))
            .Cells(Role.LineItem, (100, "B-20456"), (300, "Gurken"), (900, "3,00")));
        Assert.Equal("A-10234", items[0][Field.ArticleId].Text);
        Assert.Equal("Tomaten rot", items[0][Field.Name].Text);
    }

    [Fact]
    public void ACodeSetInTheNameColumnWithoutACellOfItsOwnIsTheArticleId()
    {
        var items = Items(Headed().Cells(Role.LineItem, (300, "10234"), (360, "Tomaten"), (900, "7,00")));
        Assert.Equal("10234", items[0][Field.ArticleId].Text);
        Assert.Equal("Tomaten", items[0][Field.Name].Text);
    }

    [Theory]
    [InlineData("Tomaten GTIN 4001234567890", "Tomaten")]
    [InlineData("Tomaten Art.-Nr 1234", "Tomaten")]
    [InlineData("Tomaten Art.-Nr.: 1234", "Tomaten")]
    [InlineData("Tomaten ArtNr 1234", "Tomaten")]
    [InlineData("Tomaten Artikelnummer 1234", "Tomaten")]
    [InlineData("Tomaten Charge 77", "Tomaten")]
    [InlineData("• Tomaten -", "Tomaten")]
    [InlineData("Tomaten rot", "Tomaten rot")]
    public void TheNameEndsWhereAKeyOpens(string cell, string name)
    {
        var items = Items(Headed().Cells(Role.LineItem, (300, cell), (900, "7,00")));
        Assert.Equal(name, items[0][Field.Name].Text);
    }

    [Fact]
    public void AWrapRowExtendsTheNameAndFillsACellTheItemLacks()
    {
        var items = Items(Headed()
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten rot"), (700, "3,50"), (900, "7,00"))
            .Cells(Role.LineWrap, (200, "kg"), (300, "extra fein"))
            .Cells(Role.Continuation, (300, "Klasse I"), (900, "8,00")));
        Row(items[0], "2", "kg", "Tomaten rot extra fein Klasse I", "3,50", "7,00");
        Assert.Equal(new Box(300, 30, 108, 80), items[0][Field.Name].Box);
    }

    [Fact]
    public void AWrapRowCarryingAKeyIsANote()
    {
        var items = Items(Headed()
            .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (900, "7,00"))
            .Cells(Role.LineWrap, (300, "Schema der Artikelkennung: 0160")));
        Assert.Equal("Tomaten", items[0][Field.Name].Text);
    }

    [Fact]
    public void AWrapRowInAColumnDecidedToMeanNothingIsDropped()
    {
        var items = Items(Headed()
            .Cells(Role.LineItem, (50, "1"), (300, "Tomaten"), (900, "7,00"))
            .Cells(Role.LineWrap, (50, "7")));
        Assert.Equal("Tomaten", items[0][Field.Name].Text);
    }

    [Fact]
    public void AWrapRowWithoutAnItemAboveItBelongsToNone()
    {
        var items = Items(Headed()
            .Cells(Role.LineWrap, (300, "Übertrag"))
            .Cells(Role.LineItem, (300, "Tomaten"), (900, "7,00"))
            .Line(Role.Total, (300, "Zwischensumme", null))
            .Cells(Role.LineWrap, (300, "Pfand")));
        Assert.Equal("Tomaten", Assert.Single(items)[Field.Name].Text);
    }

    [Fact]
    public void AWrapCellThatFitsNoColumnHangsOffTheItemAsName()
    {
        var items = Items(Headed()
            .Cells(Role.LineItem, (300, "Tomaten"), (900, "7,00"))
            .Cells(Role.LineWrap, (1100, "Bio")));
        Assert.Equal("Tomaten Bio", items[0][Field.Name].Text);
    }

    [Fact]
    public void ANumberTheScanSplitAcrossTwoCellsIsJoinedWithItsSeparator()
    {
        var items = Items(Headed().Cells(Role.LineItem, (100, "4."), (125, ",670"), (300, "Kartoffeln"), (900, "7,00")));
        Assert.Equal("4,670", items[0][Field.Quantity].Text);
    }

    [Theory]
    [InlineData("15 Stk", "15", "Stk")]
    [InlineData("6Fl", "6", "Fl")]
    [InlineData("4,5 kg", "4,5", "kg")]
    public void AQuantityAndUnitInOneCellAreSplit(string cell, string quantity, string unit)
    {
        var item = Items(Headed().Cells(Role.LineItem, (100, cell), (300, "Tomaten"), (900, "7,00")))[0];
        Assert.Equal((quantity, unit), (item[Field.Quantity].Text, item[Field.Unit].Text));
    }

    [Fact]
    public void AQuantityAndUnitInTheUnitColumnAreSplitToo()
    {
        var item = Items(Headed().Cells(Role.LineItem, (200, "15 Stk"), (300, "Tomaten"), (900, "7,00")))[0];
        Assert.Equal(("15", "Stk"), (item[Field.Quantity].Text, item[Field.Unit].Text));
    }

    [Fact]
    public void AQuantityFollowedByAWordThatIsNoUnitStaysWhole()
    {
        var item = Items(Headed().Cells(Role.LineItem, (100, "15 Tomaten"), (300, "Tomaten"), (900, "7,00")))[0];
        Assert.Equal("15 Tomaten", item[Field.Quantity].Text);
        Assert.DoesNotContain(Field.Unit, item.Keys);
    }

    // The header says net before price, which the order-only fallback would read the other way.
    static Sheet NetFirst() => new Sheet()
        .Cells(Role.ColumnHeader, (100, "Menge"), (300, "Bezeichnung"), (700, "Gesamt"), (900, "Einzelpreis"))
        .Cells(Role.LineItem, (100, "2"), (300, "Tomaten"), (700, "7,00"), (900, "3,50"));

    static Sheet Unheaded() => new Sheet().Cells(Role.LineItem, (100, "3"), (300, "Gurken"), (700, "9,99"), (900, "1,00"));

    [Fact]
    public void APageWithoutAHeaderRowContinuesThePreviousPagesColumns()
    {
        var first = Table.Read(NetFirst().Words, null);
        Assert.True(first.HasColumns);
        Row(Items(Unheaded(), first)[0], "3", null, "Gurken", "1,00", "9,99");
        Row(Items(Unheaded())[0], "3", null, "Gurken", "9,99", "1,00");
    }

    [Fact]
    public void APageWithItsOwnHeaderRowDoesNotInherit()
    {
        var first = Table.Read(NetFirst().Words, null);
        var own = Headed().Cells(Role.LineItem, (100, "3"), (300, "Gurken"), (700, "1,00"), (900, "9,99"));
        Row(Items(own, first)[0], "3", null, "Gurken", "1,00", "9,99");
    }

    [Fact]
    public void APageWithoutTableRowsHasNoColumnsAndNoItems()
    {
        var table = Table.Read(new Sheet().Line(Role.Header, (50, "Rechnung", null)).Words, null);
        Assert.False(table.HasColumns);
        Assert.Empty(table.Items);
        Assert.Empty(Table.Read([], null).Items);
    }

    [Fact]
    public void ANumberSplitAcrossTwoCellsKeepsTheSeparatorTheSecondCellBrought()
    {
        Assert.Equal("4,670", Table.Join(Field.Quantity, "4.", ",670"));
        Assert.Equal(4670, Parse.Number(Table.Join(Field.Quantity, "4.", ",670"), Parse.ScaleMilli));
    }

    [Theory]
    [InlineData(Field.Quantity, "6", ",150", "6,150")]
    [InlineData(Field.LineNet, "12", ",50", "12,50")]
    [InlineData(Field.UnitPrice, "1.", ";99", "1;99")]
    [InlineData(Field.Vat, "19", ",0", "19,0")]
    [InlineData(Field.Quantity, "4..", ",670", "4,670")]
    public void TheFirstCellNeedNotHaveKeptTheSeparator(Field field, string had, string text, string joined) =>
        Assert.Equal(joined, Table.Join(field, had, text));

    [Theory]
    [InlineData(Field.Quantity, "4.", "670", "4. 670")]
    [InlineData(Field.Quantity, "Stk", ",150", "Stk ,150")]
    [InlineData(Field.Quantity, ".", ",150", ". ,150")]
    [InlineData(Field.Name, "Bratwurst grob", ",120 g", "Bratwurst grob ,120 g")]
    [InlineData(Field.Unit, "4", ",5", "4 ,5")]
    [InlineData(Field.Quantity, "4", ",", "4 ,")]
    public void OnlyANumberContinuesANumberAndOnlyWithASeparator(Field field, string had, string text, string joined) =>
        Assert.Equal(joined, Table.Join(field, had, text));
}

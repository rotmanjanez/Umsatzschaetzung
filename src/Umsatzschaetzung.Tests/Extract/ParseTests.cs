using Umsatzschaetzung.Extract;

namespace Umsatzschaetzung.Tests.Extract;

public class ParseTests
{
    [Theory]
    [InlineData("12,50", 1250)]
    [InlineData("1.234,56", 123456)]
    [InlineData("1 234,56", 123456)]
    [InlineData("1 234,56", 123456)]
    [InlineData("24.332.16", 2433216)]
    [InlineData("12.5", 1250)]
    [InlineData("12.50", 1250)]
    [InlineData("0,05", 5)]
    [InlineData("7", 700)]
    [InlineData("12,50 €", 1250)]
    [InlineData("€ 12,50", 1250)]
    [InlineData("12,50 EUR", 1250)]
    [InlineData("12;50", 1250)]
    [InlineData("12:50", 1250)]
    [InlineData("-12,50", -1250)]
    [InlineData("12,50-", -1250)]
    [InlineData("0,005", 1)]
    [InlineData("0,004", 0)]
    public void MoneyReadsGermanSeparatorsToCents(string text, long cents) =>
        Assert.Equal(cents, Parse.Number(text, Parse.ScaleCents));

    [Theory]
    [InlineData("1.000", 1_000_000)]
    [InlineData("1.234.567", 1_234_567_000)]
    [InlineData("5,450", 5_450)]
    [InlineData("5.450", 5_450_000)]
    [InlineData("4 670", 4_670_000)]
    [InlineData("1.2345", 1_235)]
    [InlineData("134 Stk", 134_000)]
    [InlineData("0,5 kg", 500)]
    public void AThreeDigitGroupAfterADotIsThousandsAndAfterACommaDecimals(string text, long milli) =>
        Assert.Equal(milli, Parse.Number(text, Parse.ScaleMilli));

    [Theory]
    [InlineData("19 %", 1900)]
    [InlineData("7%", 700)]
    [InlineData("10,7 %", 1070)]
    public void ARateReadsInBasisPoints(string text, long bp) =>
        Assert.Equal(bp, Parse.Number(text, Parse.ScaleBp));

    [Theory]
    [InlineData("0,1234", 123_400)]
    [InlineData("0.1234", 123_400)]
    [InlineData("1.2345", 1_234_500)]
    [InlineData("1;2345", 1_234_500)]
    public void AFourDigitFractionIsReadWholeNotCutToAThousandsGroup(string text, long micro) =>
        Assert.Equal(micro, Parse.Number(text, Parse.ScaleMicro));

    [Fact]
    public void AUnitPriceReadsInMicros() => Assert.Equal(340_000, Parse.Number("0,34", Parse.ScaleMicro));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("€")]
    [InlineData("Summe")]
    [InlineData("-")]
    [InlineData("99999999999999999999999")]
    [InlineData("9999999999999999999999999999999")]
    public void TextWithoutAReadableNumberIsZero(string text) =>
        Assert.Equal(0, Parse.Number(text, Parse.ScaleMicro));

    [Theory]
    [InlineData("1O,5O €", "10,50 €")]
    [InlineData("l", "1")]
    [InlineData("I2,S0", "12,50")]
    [InlineData("ø,99", "0,99")]
    [InlineData("1O,00 EUR", "10,00 EUR")]
    [InlineData("EUR 1O,00", "EUR 10,00")]
    public void LettersInANumericCellReadAsTheDigitsTheyLookLike(string text, string digits) =>
        Assert.Equal(digits, Parse.Digits(text));

    [Theory]
    [InlineData("12 Stk")]
    [InlineData("Summe")]
    [InlineData("Brot")]
    [InlineData("12,50 €")]
    [InlineData("")]
    [InlineData("Kiste 20")]
    public void AWordOrANumberAlreadyReadableStaysAsItIs(string text) =>
        Assert.Equal(text, Parse.Digits(text));

    [Theory]
    [InlineData("08.11.2025", 2025, 11, 8)]
    [InlineData("8.1.2025", 2025, 1, 8)]
    [InlineData("08-11-2025", 2025, 11, 8)]
    [InlineData("08/11/2025", 2025, 11, 8)]
    [InlineData("2025-11-08", 2025, 11, 8)]
    [InlineData("08.11.25", 2025, 11, 8)]
    [InlineData("Rechnungsdatum: 08.11.2025", 2025, 11, 8)]
    [InlineData("  08.11.2025  ", 2025, 11, 8)]
    [InlineData("19. Dezember 2025", 2025, 12, 19)]
    [InlineData("19 dezember 2025", 2025, 12, 19)]
    [InlineData("1. März 2025", 2025, 3, 1)]
    [InlineData("29.02.2024", 2024, 2, 29)]
    public void DatesReadInGermanAndIsoShapes(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), Parse.Date(text));

    [Theory]
    [InlineData("O8.11.2O25", 2025, 11, 8)]
    [InlineData("I2.O1.25", 2025, 1, 12)]
    [InlineData("3. 0ktober 2025", 2025, 10, 3)]
    [InlineData("3. Dezemper 2025", 2025, 12, 3)]
    public void AConfusedDigitOrMonthLetterComesBack(string text, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), Parse.Date(text));

    [Theory]
    [InlineData("")]
    [InlineData("31.02.2025")]
    [InlineData("29.02.2025")]
    [InlineData("08.13.2025")]
    [InlineData("00.11.2025")]
    [InlineData("108.11.2025")]
    [InlineData("08.11.20255")]
    [InlineData("Rechnungsdatum")]
    [InlineData("3. Ju1i 2025")]
    [InlineData("3. Foo 2025")]
    [InlineData("12.50")]
    public void WhatIsNoDateIsNull(string text) => Assert.Null(Parse.Date(text));

    [Theory]
    [InlineData("Frittieröl 10 1", "Frittieröl 10 l")]
    [InlineData("Bratwurst grob, fränkisch, 120 8", "Bratwurst grob, fränkisch, 120 g")]
    [InlineData("Milch 1,5 I", "Milch 1,5 l")]
    [InlineData("Öl 5 |", "Öl 5 l")]
    public void AUnitLetterReadAsADigitAfterANumberComesBack(string name, string repaired) =>
        Assert.Equal(repaired, Parse.PackUnit(name));

    [Theory]
    [InlineData("Trg 6er Limo 8 x 0,33 l")]
    [InlineData("8 Stück Semmeln")]
    [InlineData("1 Frittieröl")]
    [InlineData("Pils 20 x 0,5 l")]
    [InlineData("Artikel 18 Stück")]
    [InlineData("Brot 10 12")]
    [InlineData("")]
    public void ANumberThatIsNotAUnitLetterIsLeftAlone(string name) =>
        Assert.Equal(name, Parse.PackUnit(name));

    [Theory]
    [InlineData("Fl", "XBO")]
    [InlineData("kg", "KGM")]
    [InlineData("Stk.", "H87")]
    [InlineData(" Stk ", "H87")]
    [InlineData("Fl Fl", "XBO")]
    [InlineData("F1", "XBO")]
    [InlineData("EI", "XBO")]
    [InlineData("17F1", "XBO")]
    [InlineData("12 Stk", "H87")]
    public void AUnitResolvesThroughAliasesAndConfusions(string text, string code) =>
        Assert.Equal(code, Parse.UnitCode(text));

    [Theory]
    [InlineData("1", "1")]
    [InlineData("0", "0")]
    [InlineData("Zi", "Zi")]
    [InlineData("KGM", "KGM")]
    [InlineData("", "")]
    public void AnUnknownShortCodeOrALoneDigitPassesThrough(string text, string code) =>
        Assert.Equal(code, Parse.UnitCode(text));

    [Theory]
    [InlineData("Kilogrammsack")]
    [InlineData("pro Stück netto")]
    public void ALongOrSpacedTextIsNoUnit(string text) => Assert.Equal("", Parse.UnitCode(text));
}

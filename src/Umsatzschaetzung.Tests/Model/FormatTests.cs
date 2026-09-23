using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class FormatTests
{
    [Fact]
    public void CentsAreWrittenGerman() => Assert.Equal("1.690,00 €", Format.Cents(169000));

    [Theory]
    [InlineData(0, "0,00 €")]
    [InlineData(5, "0,05 €")]
    [InlineData(-5, "-0,05 €")]
    [InlineData(-17_690, "-176,90 €")]
    [InlineData(151_310, "1.513,10 €")]
    [InlineData(733_595, "7.335,95 €")]
    [InlineData(123_456_789_012, "1.234.567.890,12 €")]
    public void CentsAlwaysShowTwoDecimals(long cents, string text) => Assert.Equal(text, Format.Cents(cents));

    [Theory]
    [InlineData(40_456, "404,56 %")]
    [InlineData(1900, "19 %")]
    [InlineData(550, "5,5 %")]
    [InlineData(0, "0 %")]
    [InlineData(-5, "-0,05 %")]
    [InlineData(-10_000, "-100 %")]
    public void BasisPointsArePercentWithoutTrailingZeros(long bp, string text) => Assert.Equal(text, Format.Bp(bp));

    [Theory]
    [InlineData(1_500, "1,5")]
    [InlineData(6_000, "6")]
    [InlineData(1, "0,001")]
    [InlineData(1_234_000, "1.234")]
    [InlineData(-250, "-0,25")]
    public void MilliDropsTrailingZeros(long milli, string text) => Assert.Equal(text, Format.Milli(milli));

    [Theory]
    [InlineData(13_960_000, "13,96")]
    [InlineData(1_000_000, "1,00")]
    [InlineData(1_234_567, "1,234567")]
    [InlineData(25_000, "0,025")]
    [InlineData(0, "0,00")]
    public void MicroKeepsAtLeastCents(long micro, string text) => Assert.Equal(text, Format.Micro(micro));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1_000, "1.000")]
    [InlineData(-1_234_567, "-1.234.567")]
    public void GroupSeparatesThousandsWithDots(long n, string text) => Assert.Equal(text, Format.Group(n));

    [Fact]
    public void DatesAreDayMonthYear()
    {
        Assert.Equal("08.01.2025", Format.Date(new DateOnly(2025, 1, 8)));
        Assert.Equal("", Format.Date(null));
        Assert.Equal("01.01.2024 bis 31.12.2024", Format.Period(new DateOnly(2024, 1, 1), new DateOnly(2024, 12, 31)));
    }

    [Theory]
    [InlineData(300, Unit.Ml, "300 ml")]
    [InlineData(1_000, Unit.Ml, "1 l")]
    [InlineData(50_000, Unit.Ml, "50 l")]
    [InlineData(1_500, Unit.G, "1,5 kg")]
    [InlineData(999, Unit.G, "999 g")]
    [InlineData(1_234_567, Unit.G, "1.234,567 kg")]
    [InlineData(-2_000, Unit.Ml, "-2 l")]
    [InlineData(-999, Unit.G, "-999 g")]
    [InlineData(0, Unit.G, "0 g")]
    [InlineData(1, Unit.Piece, "1 Stück")]
    [InlineData(12_345, Unit.Piece, "12.345 Stück")]
    public void QuantitiesSwitchToLitreAndKiloFromAThousand(long n, Unit unit, string text) => Assert.Equal(text, Format.Qty(n, unit));

    [Theory]
    [InlineData(0, "0 Portionen")]
    [InlineData(1, "1 Portion")]
    [InlineData(-1, "-1 Portion")]
    [InlineData(2_946, "2.946 Portionen")]
    public void PortionsAgreeInNumber(long n, string text) => Assert.Equal(text, Format.Portions(n));

    [Theory]
    [InlineData(6_000, "H87", "6 Stück")]
    [InlineData(1_500, "kg", "1,5 Kilogramm")]
    [InlineData(1, "ZZZ", "0,001 ZZZ")]
    [InlineData(6_000, "", "6")]
    public void AnInvoiceQuantityNamesItsUnit(long qty, string unit, string text) => Assert.Equal(text, Format.Quantity(qty, unit));

    [Theory]
    [InlineData(13_960_000, 1_000, "H87", "13,96 €")]
    [InlineData(13_960_000, 0, "H87", "13,96 €")]
    [InlineData(12_345_679, 10_000, "XBO", "12,345679 € je 10 Flasche")]
    [InlineData(500_000, 500, "KGM", "0,50 € je 0,5 Kilogramm")]
    public void AUnitPriceNamesItsBaseQuantityWhenItIsNotOne(long price, long baseQty, string unit, string text) =>
        Assert.Equal(text, Format.UnitPrice(price, baseQty, unit));

    [Theory]
    [InlineData(Sparte.Getränke, "Getränke")]
    [InlineData(Sparte.Speisen, "Speisen")]
    [InlineData(Sparte.Handelsware, "Handelsware")]
    [InlineData(Sparte.Unbestimmt, "Übrige")]
    public void SparteNames(Sparte s, string text) => Assert.Equal(text, Format.Sparte(s));

    [Theory]
    [InlineData(Source.Ubl, "XRechnung (UBL)")]
    [InlineData(Source.Cii, "XRechnung (CII)")]
    [InlineData(Source.Zugferd, "ZUGFeRD")]
    [InlineData(Source.Scan, "Scan")]
    public void SourceNames(Source s, string text) => Assert.Equal(text, Format.Source(s));

    [Theory]
    [InlineData(Entity.Category, "Kategorie")]
    [InlineData(Entity.Ingredient, "Zutat")]
    [InlineData(Entity.Mapping, "Zuordnung")]
    [InlineData(Entity.Product, "Produkt")]
    [InlineData(Entity.YieldRule, "Ausbeuteregel")]
    public void EntityNames(Entity e, string text) => Assert.Equal(text, Format.EntityName(e));

    [Fact]
    public void TimestampsAreLocalAndEmptyWhenUnset()
    {
        var local = new DateTime(2025, 1, 8, 14, 5, 0, DateTimeKind.Local);
        var at = new DateTimeOffset(local);
        Assert.Equal("08.01.2025 14:05", Format.Timestamp(at));
        Assert.Equal("08.01.2025", Format.Day(at));
        Assert.Equal("automatisch geprüft am 08.01.2025 14:05", Format.Verified(at, true));
        Assert.Equal("geprüft am 08.01.2025 14:05", Format.Verified(at, false));
        Assert.Equal("", Format.Timestamp(default));
        Assert.Equal("", Format.Day(default));
    }
}

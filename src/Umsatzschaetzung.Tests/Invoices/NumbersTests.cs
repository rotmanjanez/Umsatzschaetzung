using Umsatzschaetzung.Invoices;

namespace Umsatzschaetzung.Tests.Invoices;

public class NumbersTests
{
    [Theory]
    [InlineData("12.34", 1234)]
    [InlineData("12.3", 1230)]
    [InlineData("12", 1200)]
    [InlineData("12.", 1200)]
    [InlineData(".5", 50)]
    [InlineData("0.00", 0)]
    [InlineData("-0.00", 0)]
    [InlineData("+7.10", 710)]
    [InlineData("-7.10", -710)]
    [InlineData("  83.76 \n", 8376)]
    [InlineData("007.00", 700)]
    [InlineData("12.3400", 1234)]
    public void CentsReadADotDecimal(string s, long cents) => Assert.Equal(cents, Numbers.Cents(s));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("-")]
    [InlineData("+-1")]
    [InlineData("1,50")]
    [InlineData("1 000.00")]
    [InlineData("1e3")]
    [InlineData("0x10")]
    [InlineData("1.2.3")]
    [InlineData("EUR 5")]
    [InlineData("١٢")]
    [InlineData("99999999999999999999")]
    public void CentsRejectWhatIsNotAPlainDecimal(string s) =>
        Assert.Throws<InvalidDataException>(() => Numbers.Cents(s));

    [Fact]
    public void CentsNeverRoundAwayACent()
    {
        var e = Assert.Throws<InvalidDataException>(() => Numbers.Cents("1.005"));
        Assert.Equal("number \"1.005\" has more than 2 decimals", e.Message);
    }

    [Theory]
    [InlineData("6.00", 6000)]
    [InlineData("0.125", 125)]
    [InlineData("400.0000", 400_000)]
    [InlineData("-2.5", -2500)]
    public void MilliScaleQuantitiesByAThousand(string s, long milli) => Assert.Equal(milli, Numbers.Milli(s));

    [Fact]
    public void MilliRejectsAFourthSignificantDecimal() =>
        Assert.Throws<InvalidDataException>(() => Numbers.Milli("1.0005"));

    [Theory]
    [InlineData("13.96", 13_960_000)]
    [InlineData("0.0250", 25_000)]
    [InlineData("12.3456789", 12_345_679)]
    [InlineData("12.3456784", 12_345_678)]
    [InlineData("0.0000005", 1)]
    [InlineData("0.0000004", 0)]
    [InlineData("0.9999995", 1_000_000)]
    [InlineData("-0.0000005", -1)]
    [InlineData("-1.2345675", -1_234_568)]
    public void MicroRoundsHalfAwayFromZeroBeyondSixDecimals(string s, long micro) =>
        Assert.Equal(micro, Numbers.Micro(s));

    [Theory]
    [InlineData("19", 1900)]
    [InlineData("19.00", 1900)]
    [InlineData("7", 700)]
    [InlineData("5.5", 550)]
    [InlineData("0", 0)]
    public void BpReadAPercentInBasisPoints(string percent, long bp) => Assert.Equal(bp, Numbers.Bp(percent));

    [Theory]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("1.50", 150)]
    public void OptionalCentsTreatBlankAsZero(string s, long cents) => Assert.Equal(cents, Numbers.OptionalCents(s));

    [Theory]
    [InlineData("", 0)]
    [InlineData("19", 1900)]
    public void OptionalBpTreatsBlankAsZero(string s, long bp) => Assert.Equal(bp, Numbers.OptionalBp(s));

    [Theory]
    [InlineData("", 1000)]
    [InlineData(" ", 1000)]
    [InlineData("1", 1000)]
    [InlineData("10", 10_000)]
    [InlineData("0.5", 500)]
    public void ABlankBaseQuantityIsOne(string s, long milli) => Assert.Equal(milli, Numbers.BaseQty(s));

    [Fact]
    public void AnOptionalNumberStillRejectsGarbage()
    {
        Assert.Throws<InvalidDataException>(() => Numbers.OptionalCents("n/a"));
        Assert.Throws<InvalidDataException>(() => Numbers.BaseQty("n/a"));
    }

    [Theory]
    [InlineData("20250108", 2025, 1, 8)]
    [InlineData(" 2025-01-08 ", 2025, 1, 8)]
    public void OptionalDateTriesEachFormat(string s, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), Numbers.OptionalDate(s, "yyyyMMdd", "yyyy-MM-dd"));

    [Theory]
    [InlineData("")]
    [InlineData("20251308")]
    [InlineData("08.01.2025")]
    [InlineData("2025-02-30")]
    public void AnUnreadableDateIsAbsent(string s) => Assert.Null(Numbers.OptionalDate(s, "yyyyMMdd", "yyyy-MM-dd"));

    [Theory]
    [InlineData("3", 0, 3)]
    [InlineData(" 12 ", 0, 12)]
    [InlineData("", 4, 5)]
    [InlineData("0", 4, 5)]
    [InlineData("-2", 4, 5)]
    [InlineData("1.1", 4, 5)]
    [InlineData("A1", 4, 5)]
    public void ALineNumberFallsBackToThePosition(string id, int index, long no) =>
        Assert.Equal(no, Numbers.LineNo(id, index));

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData(" A-17 ", "A-17")]
    public void OptionalTextIsTrimmedOrNull(string s, string? expected) => Assert.Equal(expected, Numbers.Optional(s));

    [Fact]
    public void WrapPrefixesTheContextAndKeepsTheCause()
    {
        var e = Assert.Throws<InvalidDataException>(() => Numbers.Wrap("line 2", () => Numbers.Cents("x")));
        Assert.Equal("line 2: invalid number \"x\"", e.Message);
        Assert.IsType<InvalidDataException>(e.InnerException);
    }

    [Fact]
    public void WrapLetsOtherExceptionsThrough() =>
        Assert.Throws<InvalidOperationException>(() => Numbers.Wrap<int>("ctx", () => throw new InvalidOperationException()));
}

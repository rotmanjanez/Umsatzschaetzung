using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class InvoiceMathTests
{
    [Theory]
    [InlineData(5, 10, 1)]
    [InlineData(4, 10, 0)]
    [InlineData(15, 10, 2)]
    [InlineData(-5, 10, -1)]
    [InlineData(-4, 10, 0)]
    [InlineData(-15, 10, -2)]
    [InlineData(0, 10, 0)]
    [InlineData(20, 10, 2)]
    [InlineData(7, 3, 2)]
    public void RoundDivRoundsHalfAwayFromZero(long num, long den, long expected) =>
        Assert.Equal(expected, InvoiceMath.RoundDiv(num, den));

    [Theory]
    [InlineData(6_000, 13_960_000, 1000, 8_376)]
    [InlineData(20_500, 12_345_679, 10_000, 2_531)]
    [InlineData(800_000, 25_000, 1000, 2_000)]
    [InlineData(1_000, 5_000, 1000, 1)]
    [InlineData(1_000, 4_999, 1000, 0)]
    [InlineData(-1_000, 5_000, 1000, -1)]
    [InlineData(-2_000, 1_990_000, 1000, -398)]
    [InlineData(0, 1_990_000, 1000, 0)]
    public void LineNetIsQuantityTimesPriceOverBaseInCents(long quantity, long unitPrice, long baseQty, long net) =>
        Assert.Equal(net, InvoiceMath.LineNet(quantity, unitPrice, baseQty));

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public void ANonPositiveBaseQuantityCountsAsOne(long baseQty) =>
        Assert.Equal(InvoiceMath.LineNet(6_000, 13_960_000, 1000), InvoiceMath.LineNet(6_000, 13_960_000, baseQty));

    [Fact]
    public void LineNetHoldsForLargeWholesaleQuantities() =>
        Assert.Equal(99_999_999_800, InvoiceMath.LineNet(999_999_999, 999_999_999, 1000));

    static InvoiceLine Line(long net, long vat) => new() { LineNet = net, Vat = vat };

    [Fact]
    public void NoLinesAddUpToNothing() => Assert.Equal((0L, 0L), InvoiceMath.LineTotals([]));

    [Fact]
    public void VatIsAddedPerRate() =>
        Assert.Equal((29_735L, 32_055L), InvoiceMath.LineTotals([Line(27_745, 700), Line(1_990, 1900)]));

    [Fact]
    public void VatIsRoundedOnTheSumOfARateNotPerLine() =>
        Assert.Equal((6L, 7L), InvoiceMath.LineTotals([Line(2, 1900), Line(2, 1900), Line(2, 1900)]));

    [Fact]
    public void ACentBoundaryRoundsUp()
    {
        Assert.Equal((50L, 54L), InvoiceMath.LineTotals([Line(50, 700)]));
        Assert.Equal((150L, 161L), InvoiceMath.LineTotals([Line(150, 700)]));
        Assert.Equal((149L, 159L), InvoiceMath.LineTotals([Line(149, 700)]));
    }

    [Fact]
    public void ACreditLineReducesNetAndVat() =>
        Assert.Equal((5_000L, 5_950L), InvoiceMath.LineTotals([Line(10_000, 1900), Line(-5_000, 1900)]));

    [Fact]
    public void ANegativeRateSumRoundsAwayFromZero() =>
        Assert.Equal((-150L, -161L), InvoiceMath.LineTotals([Line(-150, 700)]));

    [Fact]
    public void ZeroRatedLinesAddNoVat() =>
        Assert.Equal((1_234L, 1_234L), InvoiceMath.LineTotals([Line(1_000, 0), Line(234, 0)]));
}

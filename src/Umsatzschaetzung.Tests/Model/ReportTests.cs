using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class ReportTests
{
    [Theory]
    [InlineData(733_595, 145_391, 40_456)]
    [InlineData(20_000, 10_000, 10_000)]
    [InlineData(10_000, 10_000, 0)]
    [InlineData(5_000, 10_000, -5_000)]
    [InlineData(0, 10_000, -10_000)]
    [InlineData(20_000, 4, 49_990_000)]
    public void RohaufschlagIsTheSurplusOverTheCostInBasisPoints(long revenue, long cost, long bp) =>
        Assert.Equal(bp, Rohaufschlag.Of(revenue, cost));

    [Theory]
    [InlineData(10_001, 3, 33_326_666)]
    [InlineData(2, 3, -3_333)]
    public void RohaufschlagDropsAFractionOfABasisPoint(long revenue, long cost, long bp) =>
        Assert.Equal(bp, Rohaufschlag.Of(revenue, cost));

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void WithoutCostThereIsNoRohaufschlag(long cost) => Assert.Equal(0, Rohaufschlag.Of(50_000, cost));

    [Fact]
    public void TotalsDeriveShrinkageRemainderProfitAndMarkup()
    {
        var t = new Totals { CostOfGoods = 151_310, SellableCost = 145_395, AllocatedCost = 145_391, CalculatedRevenueNet = 733_595 };
        Assert.Equal(5_915, t.ShrinkageCost);
        Assert.Equal(4, t.UnallocatedCost);
        Assert.Equal(588_204, t.GrossProfit);
        Assert.Equal(40_456, t.Markup);
    }

    [Fact]
    public void MarkupAndProductRowsUseTheSameRohaufschlag()
    {
        var m = new MarkupRow { CostOfGoods = 145_391, RevenueNet = 733_595 };
        Assert.Equal((588_204L, 40_456L), (m.GrossProfit, m.Markup));
        var p = new ProductRow { CostOfGoods = 55, RevenueNet = 266 };
        Assert.Equal(38_363, p.Markup);
        Assert.Equal(0, new ProductRow { RevenueNet = 266 }.Markup);
    }

    static ProductRow Row(long vat, long net, bool disabled = false) => new() { Vat = vat, RevenueNet = net, Disabled = disabled };

    [Fact]
    public void VatRowsCompareDeclaredAndCalculatedPerRateThenInTotal()
    {
        var c = new Case { Declared = [new() { Vat = 1900, Net = 500_000 }, new() { Vat = 700, Net = 80_000 }, new() { Vat = 1900, Net = 20_000 }] };
        var r = new Report { Products = [Row(700, 90_000), Row(1900, 600_000), Row(1900, 1_000, disabled: true), Row(1300, 7_000), Row(500, 3_000)] };
        var rows = VatRow.Of(c, r);
        Assert.Equal(
            [new(1900, 520_000, 600_000, false), new(700, 80_000, 90_000, false), new(500, 0, 3_000, false), new(1300, 0, 7_000, false),
             new(0, 600_000, 700_000, true)],
            rows);
        Assert.Equal(80_000, rows[0].Difference);
        Assert.Equal(100_000, rows[^1].Difference);
    }

    [Fact]
    public void OnlyRatesWithRevenueGetARow()
    {
        var empty = VatRow.Of(new Case(), new Report());
        Assert.Equal([new VatRow(0, 0, 0, true)], empty);
        var zero = VatRow.Of(new Case { Declared = [new() { Vat = 0, Net = 1_000 }] }, new Report());
        Assert.Equal([new VatRow(0, 1_000, 0, false), new VatRow(0, 1_000, 0, true)], zero);
    }

    [Fact]
    public void ADeclaredRateTheCalculationDoesNotKnowStillCounts()
    {
        var c = new Case { Declared = [new() { Vat = 1600, Net = 1_000 }, new() { Vat = 500, Net = 2_000 }, new() { Vat = 1900, Net = 3_000 }] };
        var rows = VatRow.Of(c, new Report { Products = [Row(1900, 4_000), Row(500, 500)] });
        Assert.Equal(
            [new(1900, 3_000, 4_000, false), new(500, 2_000, 500, false), new(1600, 1_000, 0, false), new(0, 6_000, 4_500, true)],
            rows);
    }
}

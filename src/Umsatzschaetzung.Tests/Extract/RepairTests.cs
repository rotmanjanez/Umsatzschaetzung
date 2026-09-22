using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Extract;

public class RepairTests
{
    static InvoiceLine Line(long quantity, long unitPrice, long lineNet) =>
        new() { Quantity = quantity, UnitPrice = unitPrice, LineNet = lineNet, PriceBaseQty = 1000 };

    static InvoiceLine Regrouped(string text, long quantity, long unitPrice, long lineNet)
    {
        var line = Line(quantity, unitPrice, lineNet);
        Assemble.Regroup(line, text);
        return line;
    }

    static InvoiceLine Repaired(long quantity, long unitPrice, long lineNet)
    {
        var line = Line(quantity, unitPrice, lineNet);
        Assemble.Repair(line);
        return line;
    }

    [Theory]
    [InlineData("5.450", 5_450_000, 12_000_000, 6540, 5450)]
    [InlineData("5,450", 5_450_000, 12_000_000, 6540, 5450)]
    [InlineData("4 670", 4_670_000, 6_900_000, 3222, 4670)]
    [InlineData(" 4 670 ", 4_670_000, 6_900_000, 3222, 4670)]
    public void ALostDecimalCommaIsTakenBackByTheLineNet(string text, long quantity, long price, long net, long expected) =>
        Assert.Equal(expected, Regrouped(text, quantity, price, net).Quantity);

    [Theory]
    [InlineData("134", 134_000, 340_000, 4556)]
    [InlineData("1.000", 1_000_000, 500_000, 50_000)]
    [InlineData("5450", 5_450_000, 12_000_000, 6540)]
    [InlineData("1,25", 1250, 12_000_000, 1500)]
    [InlineData("5,450", 5_450_000, 12_000_000, 0)]
    [InlineData("5,450", 5_450_000, 0, 6540)]
    [InlineData("5,450", 5_450_000, 12_000_000, 9999)]
    [InlineData("12.450,5", 12_450_500, 1_000_000, 1245)]
    public void OnlyTheAmbiguousShapeIsRegroupedAndOnlyWhenTheRowSaysSo(string text, long quantity, long price, long net) =>
        Assert.Equal(quantity, Regrouped(text, quantity, price, net).Quantity);

    // "5,450" already parses as 5,45: dividing that again would invent 0,005, a quantity never printed.
    [Fact]
    public void AQuantityAlreadyReadAsADecimalIsNeverDividedAgain() =>
        Assert.Equal(5450, Regrouped("5,450", 5450, 2_000_000, 1).Quantity);

    [Fact]
    public void RegroupHonoursThePriceBase()
    {
        var line = new InvoiceLine { Quantity = 5_450_000, UnitPrice = 1_200_000_000, PriceBaseQty = 100_000, LineNet = 6540 };
        Assemble.Regroup(line, "5.450");
        Assert.Equal(5450, line.Quantity);
    }

    [Fact]
    public void ANineReadAsASixInTheQuantityIsPutBackByTheRow() =>
        Assert.Equal(9000, Repaired(6000, 900_000, 810).Quantity);

    [Fact]
    public void ASixReadAsAFiveInTheUnitPriceIsPutBack() =>
        Assert.Equal(6_900_000, Repaired(3750, 5_900_000, 2588).UnitPrice);

    [Fact]
    public void ASixReadAsASevenInTheLineNetIsPutBack() =>
        Assert.Equal(8457, Repaired(13640, 6_200_000, 8467).LineNet);

    [Fact]
    public void ARowThatAddsUpIsNeverTouched()
    {
        var line = Repaired(134_000, 340_000, 4556);
        Assert.Equal((134_000, 340_000, 4556), (line.Quantity, line.UnitPrice, line.LineNet));
    }

    // 6000 × 1,00 reads 8,00: the eight could be the line net (6,00) or the quantity (8000).
    [Fact]
    public void TwoDigitsExplainingTheRowEquallyWellLeaveItAsRead()
    {
        var line = Repaired(6000, 1_000_000, 800);
        Assert.Equal((6000, 1_000_000, 800), (line.Quantity, line.UnitPrice, line.LineNet));
    }

    [Fact]
    public void NoSingleDigitExplainingTheRowLeavesItAsRead()
    {
        var line = Repaired(5000, 1_234_567, 9999);
        Assert.Equal((5000, 1_234_567, 9999), (line.Quantity, line.UnitPrice, line.LineNet));
    }

    // 81 × 1,00 reads 1,00: only 01 would add up, and nobody prints a leading zero.
    [Fact]
    public void ALeadingDigitIsNeverRepairedToZero() =>
        Assert.Equal(81_000, Repaired(81_000, 1_000_000, 100).Quantity);

    [Theory]
    [InlineData(1000, 1_000_000, 200)]
    [InlineData(2000, 1_000_000, 300)]
    public void DigitsThatDoNotShareAShapeAreNotConfused(long quantity, long price, long net)
    {
        var line = Repaired(quantity, price, net);
        Assert.Equal((quantity, price, net), (line.Quantity, line.UnitPrice, line.LineNet));
    }

    [Fact]
    public void ACreditLineIsNotRepairedByConfusion()
    {
        var line = Repaired(6000, 900_000, -810);
        Assert.Equal((6000, 900_000, -810), (line.Quantity, line.UnitPrice, line.LineNet));
    }

    [Fact]
    public void AQuantityTheScanLostComesBackFromPriceAndNet() =>
        Assert.Equal(9000, Repaired(0, 900_000, 810).Quantity);

    [Fact]
    public void ALostQuantityComesBackOnlyWhereTheDivisionIsClean() =>
        Assert.Equal(0, Repaired(0, 3_000_000, 1000).Quantity);

    [Fact]
    public void ALostQuantityComesBackOnlyToTheHundredth() =>
        Assert.Equal(0, Repaired(0, 10_000_000, 1).Quantity);

    [Fact]
    public void ALostUnitPriceComesBackInWholeCents() =>
        Assert.Equal(4_300_000, Repaired(3000, 0, 1290).UnitPrice);

    [Fact]
    public void AUnitPriceThatIsNotWholeCentsIsNotOne() =>
        Assert.Equal(0, Repaired(3000, 0, 1145).UnitPrice);

    [Fact]
    public void TheLineNetIsNeverDerived() =>
        Assert.Equal(0, Repaired(3000, 4_300_000, 0).LineNet);

    [Fact]
    public void BothQuantityAndPriceLostLeaveTheRowAsRead()
    {
        var line = Repaired(0, 0, 1000);
        Assert.Equal((0, 0), (line.Quantity, line.UnitPrice));
    }

    // 46,92 over 58 kg is no price at all, over the 68 kg printed it is 0,69.
    [Fact]
    public void TheSurvivingCellIsReadAgainWhereOnlyOneConfusionOfItDividesOut()
    {
        var line = Repaired(58_000, 0, 4692);
        Assert.Equal((68_000, 690_000), (line.Quantity, line.UnitPrice));
    }

    // 12,00 over 7 reads nothing; over 1 it is 12,00 and over 2 it is 6,00.
    [Fact]
    public void TwoConfusionsDividingOutEquallyWellLeaveTheRowAsRead()
    {
        var line = Repaired(7000, 0, 1200);
        Assert.Equal((7000, 0), (line.Quantity, line.UnitPrice));
    }

    // A negative cell was read, not lost: restoring over it would flip the sign the scan printed.
    [Theory]
    [InlineData(-2000, 5_000_000, 1000)]
    [InlineData(2000, -5_000_000, 1000)]
    public void ANegativeCellIsNotRestoredAsIfItWereLost(long quantity, long price, long net)
    {
        var line = Repaired(quantity, price, net);
        Assert.Equal((quantity, price, net), (line.Quantity, line.UnitPrice, line.LineNet));
    }

    [Fact]
    public void RestoreHonoursThePriceBase()
    {
        var line = new InvoiceLine { Quantity = 3000, UnitPrice = 0, PriceBaseQty = 100_000, LineNet = 1290 };
        Assemble.Repair(line);
        Assert.Equal(430_000_000, line.UnitPrice);
    }
}

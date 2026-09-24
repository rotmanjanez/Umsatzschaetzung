using Umsatzschaetzung.Model;
using Checks = Umsatzschaetzung.Extract.Check;

namespace Umsatzschaetzung.Tests.Extract;

public class CheckTests
{
    static InvoiceLine Line(long no, long net, long vat = 1900, string unit = "KGM") => new()
    {
        No = no, Name = "Ware", Quantity = 1000, UnitPrice = net * 10_000, PriceBaseQty = 1000, LineNet = net, Vat = vat, UnitCode = unit,
    };

    static Invoice Clean() => new()
    {
        Number = "RE-1",
        SupplierName = "Metro",
        Date = new DateOnly(2025, 1, 1),
        StatedNet = 500,
        StatedGross = 595,
        Lines = [Line(1, 500)],
    };

    static Flag Single(List<Flag> flags, string code) => Assert.Single(flags, f => f.Code == code);

    [Fact]
    public void ALineThatStatesItsUnitAndAddsUpIsClean()
    {
        var inv = Clean();
        var flags = Checks.Invoice(inv);
        Assert.Empty(flags);
        Assert.True(Checks.Complete(inv, flags));
    }

    [Fact]
    public void ALineWithoutAUnitIsReportedAndTheInvoiceIsNotComplete()
    {
        var inv = Clean();
        inv.Lines[0].UnitCode = "";
        var flags = Checks.Invoice(inv);
        var flag = Single(flags, "no_unit");
        Assert.Equal((1L, (Field?)Field.Unit), (flag.LineNo, flag.Field));
        Assert.False(Checks.Complete(inv, flags));
        Assert.False(Checks.Complete(new Invoice(), flags));
    }

    [Fact]
    public void AZeroQuantityIsReportedOnItsLineAndField()
    {
        var inv = Clean();
        inv.Lines[0].Quantity = 0;
        inv.Lines[0].LineNet = 0;
        inv.StatedNet = 0;
        var flag = Assert.Single(Checks.Invoice(inv), f => f.Code == "zero");
        Assert.Equal((1L, (Field?)Field.Quantity), (flag.LineNo, flag.Field));
    }

    [Fact]
    public void AFreeLineIsCompleteLikeAnyOther()
    {
        var inv = Clean();
        inv.Lines.Add(new InvoiceLine
        {
            No = 2, Name = "Gratis", Quantity = 1000, UnitPrice = 0, PriceBaseQty = 1000, LineNet = 0, Vat = 1900, UnitCode = "C62",
        });
        var flags = Checks.Invoice(inv);
        Assert.Empty(flags);
        Assert.True(Checks.Complete(inv, flags));
    }

    [Fact]
    public void AFreeLineWithAPriceIsReported()
    {
        var inv = Clean();
        inv.Lines.Add(new InvoiceLine
        {
            No = 2, Name = "Gratis", Quantity = 1000, UnitPrice = 2_000_000, PriceBaseQty = 1000, LineNet = 0, Vat = 1900, UnitCode = "C62",
        });
        Assert.Contains(Checks.Invoice(inv), f => f.Code == "line_total" && f.LineNo == 2);
    }

    [Theory]
    [InlineData(-2000, 250_000)]
    [InlineData(2000, -250_000)]
    public void ANegativeLineThatAddsUpIsCompleteLikeAnyOther(long quantity, long unitPrice)
    {
        var inv = Clean();
        inv.StatedNet = 450;
        inv.StatedGross = 536;
        inv.Lines.Add(new InvoiceLine
        {
            No = 2, Name = "Pfand", Quantity = quantity, UnitPrice = unitPrice, PriceBaseQty = 1000, LineNet = -50, Vat = 1900, UnitCode = "C62",
        });
        var flags = Checks.Invoice(inv);
        Assert.Empty(flags);
        Assert.True(Checks.Complete(inv, flags));
    }

    [Fact]
    public void ANegativeLineThatLostItsSignIsReported()
    {
        var inv = Clean();
        inv.StatedNet = 450;
        inv.StatedGross = 536;
        inv.Lines.Add(new InvoiceLine
        {
            No = 2, Name = "Pfand", Quantity = 2000, UnitPrice = 250_000, PriceBaseQty = 1000, LineNet = -50, Vat = 1900, UnitCode = "C62",
        });
        Assert.Contains(Checks.Invoice(inv), f => f.Code == "line_total" && f.LineNo == 2);
    }

    [Fact]
    public void QuantityTimesPriceMissingTheLineNetIsReportedOnAllThreeWithBothAmounts()
    {
        var inv = Clean();
        inv.Lines[0].UnitPrice = 4_990_000;
        var flags = Checks.Invoice(inv).FindAll(f => f.Code == "line_total");
        Assert.Equal(
            [(1L, Field.Quantity), (1L, Field.UnitPrice), (1L, Field.LineNet)],
            flags.Select(f => (f.LineNo, f.Field!.Value)));
        Assert.All(flags, f => Assert.Contains(Format.Cents(499), f.Message));
        Assert.All(flags, f => Assert.Contains(Format.Cents(500), f.Message));
    }

    [Fact]
    public void TheLineCheckHonoursThePriceBase()
    {
        var inv = Clean();
        inv.StatedNet = 5000;
        inv.StatedGross = 5950;
        inv.Lines[0] = new InvoiceLine
        {
            No = 1, Quantity = 5000, UnitPrice = 1_000_000_000, PriceBaseQty = 100_000, LineNet = 5000, Vat = 1900, UnitCode = "KGM",
        };
        Assert.Empty(Checks.Invoice(inv));
    }

    [Fact]
    public void PositionsNotAddingUpToTheStatedNetAreReportedOnTheNetAndEveryLineNet()
    {
        var inv = Clean();
        inv.Lines.Add(Line(2, 100));
        Assert.Equal(
            [(0L, Field.NetTotal), (1L, Field.LineNet), (2L, Field.LineNet)],
            Checks.Invoice(inv).Where(f => f.Code == "sum_net").Select(f => (f.LineNo, f.Field!.Value)));
    }

    [Fact]
    public void WithoutAStatedNetTheComputedNetIsChecked()
    {
        var inv = Clean();
        inv.StatedNet = null;
        inv.StatedGross = null;
        inv.NetTotal = 500;
        inv.GrossTotal = 595;
        var flags = Checks.Invoice(inv);
        Assert.Empty(flags);
        Assert.False(Checks.Complete(inv, flags));
        inv.NetTotal = 400;
        Assert.Contains(Checks.Invoice(inv), f => f.Code == "sum_net");
    }

    [Fact]
    public void NonPositiveTotalsAreReportedWithoutALine()
    {
        var inv = new Invoice { StatedNet = 0, StatedGross = -5 };
        var flags = Checks.Invoice(inv);
        Assert.Equal(2, flags.Count);
        Assert.All(flags, f => Assert.Equal(("nonpositive", 0L), (f.Code, f.LineNo)));
        Assert.Equal([Field.NetTotal, Field.GrossTotal], flags.Select(f => f.Field!.Value));
    }

    [Fact]
    public void AnInvoiceWithoutLinesHasNoSumToMiss() =>
        Assert.DoesNotContain(Checks.Invoice(new Invoice { StatedNet = 500, StatedGross = 595 }), f => f.Code == "sum_net");

    [Fact]
    public void AGrossTotalOffTheRateIsReported()
    {
        var inv = Clean();
        inv.StatedGross = 600;
        var flags = Checks.Invoice(inv).FindAll(f => f.Code == "gross_check");
        Assert.Equal(
            [(0L, Field.GrossTotal), (0L, Field.NetTotal), (1L, Field.Vat)],
            flags.Select(f => (f.LineNo, f.Field!.Value)));
        Assert.All(flags, f => Assert.Contains(Format.Cents(595), f.Message));
    }

    // Two lines of 0,50 at 7 %: once on the total 1,07, per position 0,04 + 0,04 = 1,08.
    [Theory]
    [InlineData(107, true)]
    [InlineData(108, true)]
    [InlineData(106, false)]
    [InlineData(109, false)]
    public void TaxRoundedOnTheTotalOrPerPositionBothCount(long gross, bool clean)
    {
        var inv = Clean();
        inv.StatedNet = 100;
        inv.StatedGross = gross;
        inv.Lines = [Line(1, 50, 700), Line(2, 50, 700)];
        Assert.Equal(clean, !Checks.Invoice(inv).Any(f => f.Code == "gross_check"));
    }

    // 5,00 at 19 % and 5,00 at 7 % make 11,30; a rate that was never read counts as 0 %.
    [Theory]
    [InlineData(1900, 700, 1130, true)]
    [InlineData(1900, 700, 9999, false)]
    [InlineData(0, 0, 1000, true)]
    [InlineData(0, 0, 1130, false)]
    [InlineData(700, 0, 1130, false)]
    public void MixedAndZeroRatesAreCheckedAgainstTheGross(long first, long second, long gross, bool clean)
    {
        var inv = Clean();
        inv.StatedNet = 1000;
        inv.StatedGross = gross;
        inv.Lines = [Line(1, 500, first), Line(2, 500, second)];
        Assert.Equal(clean, !Checks.Invoice(inv).Any(f => f.Code == "gross_check"));
    }

    [Fact]
    public void EveryLineIsCheckedOnItsOwn()
    {
        var inv = Clean();
        inv.StatedNet = 1000;
        inv.StatedGross = 1190;
        inv.Lines = [Line(1, 500, unit: ""), Line(2, 500, unit: "")];
        Assert.Equal([1L, 2L], Checks.Invoice(inv).Where(f => f.Code == "no_unit").Select(f => f.LineNo));
    }

    public static TheoryData<string> Missing => ["supplier", "number", "date", "net", "gross", "lines"];

    [Theory]
    [MemberData(nameof(Missing))]
    public void AnInvoiceIsCompleteOnlyWithEveryHeaderFieldAndItsPrintedTotals(string missing)
    {
        var inv = Clean();
        switch (missing)
        {
            case "supplier": inv.SupplierName = ""; break;
            case "number": inv.Number = ""; break;
            case "date": inv.Date = null; break;
            case "net": inv.StatedNet = null; break;
            case "gross": inv.StatedGross = null; break;
            case "lines": inv.Lines.Clear(); break;
        }
        Assert.False(Checks.Complete(inv, []));
    }

    [Fact]
    public void AnyFlagStopsCompleteness() =>
        Assert.False(Checks.Complete(Clean(), [new Flag { Code = "gross_check" }]));

    [Theory]
    [InlineData("line_total", 500L, true)]
    [InlineData("line_total", null, true)]
    [InlineData("sum_net", null, true)]
    [InlineData("sum_net", 500L, false)]
    [InlineData("gross_check", null, false)]
    [InlineData("no_unit", null, false)]
    [InlineData("nonpositive", null, false)]
    public void OnlyTheReadersOwnArithmeticBlocks(string code, long? statedNet, bool blocks) =>
        Assert.Equal(blocks, Checks.Blocks(new Invoice { StatedNet = statedNet }, new Flag { Code = code }));
}

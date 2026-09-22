using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class IncludedTests
{
    static Invoice Inv(string id, string number, DateOnly? date, params long[] lines) => new()
    {
        Id = id,
        Number = number,
        FileName = id + ".xml",
        Date = date,
        Lines = [.. lines.Select(n => new InvoiceLine { No = n, Name = $"{id}/{n}" })],
    };

    [Fact]
    public void UnmappedAndUnusedLinesAreLeftOut()
    {
        var c = new Case { Invoices = [Inv("a", "RE-1", new(2025, 1, 8), 1, 2, 3, 4)] };
        var r = new Report
        {
            Unmapped = [new() { InvoiceId = "a", LineNo = 2 }],
            Unused = [new() { InvoiceId = "a", LineNo = 4 }, new() { InvoiceId = "b", LineNo = 1 }],
        };
        var inc = Assert.Single(Included.Of(c, r));
        Assert.Equal(("RE-1", "a.xml", (DateOnly?)new DateOnly(2025, 1, 8)), (inc.Number, inc.FileName, inc.Date));
        Assert.Equal([1L, 3], inc.Lines.Select(l => l.No));
    }

    [Fact]
    public void AnInvoiceWithNothingLeftIsDropped()
    {
        var c = new Case { Invoices = [Inv("a", "RE-1", null, 1), Inv("b", "RE-2", null, 1), Inv("c", "RE-3", null)] };
        var r = new Report { Unmapped = [new() { InvoiceId = "a", LineNo = 1 }] };
        Assert.Equal(["RE-2"], Included.Of(c, r).Select(i => i.Number));
    }

    [Fact]
    public void InvoicesAreOrderedByDateThenNumber()
    {
        var c = new Case
        {
            Invoices =
            [
                Inv("a", "RE-9", new(2025, 2, 1), 1),
                Inv("b", "RE-10", new(2025, 1, 8), 1),
                Inv("c", "RE-2", new(2025, 1, 8), 1),
                Inv("d", "RE-5", null, 1),
            ],
        };
        Assert.Equal(["RE-5", "RE-10", "RE-2", "RE-9"], Included.Of(c, new Report()).Select(i => i.Number));
    }

    [Fact]
    public void TheSameLineNumberOnAnotherInvoiceStaysIn()
    {
        var c = new Case { Invoices = [Inv("a", "RE-1", null, 1), Inv("b", "RE-2", null, 1)] };
        var r = new Report { Unused = [new() { InvoiceId = "a", LineNo = 1 }] };
        Assert.Equal(["b/1"], Included.Of(c, r).SelectMany(i => i.Lines).Select(l => l.Name));
    }
}

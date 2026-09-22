using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Invoices;

public class FixtureTests
{
    public static TheoryData<string> EInvoices => new(
        Directory.EnumerateFiles(TestData.Fixture("dataset"), "*.xml", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(TestData.Fixture(""), f))
            .Order(StringComparer.Ordinal));

    [Theory]
    [MemberData(nameof(EInvoices))]
    public void ARealEInvoiceParsesAsExpected(string relative)
    {
        var path = TestData.Fixture(relative);
        var want = Json.Deserialize<Umsatzschaetzung.Model.Invoice>(File.ReadAllBytes(path + ".expected.json"));
        var got = InvoiceParser.Parse(Path.GetFileName(path), File.ReadAllBytes(path));

        Assert.Equal(
            (want.Source, want.FileName, want.SupplierName, want.Number, want.Date, want.Currency, want.NetTotal, want.GrossTotal),
            (got.Source, got.FileName, got.SupplierName, got.Number, got.Date, got.Currency, got.NetTotal, got.GrossTotal));
        Assert.Equal(want.Lines.Count, got.Lines.Count);
        foreach (var (w, g) in want.Lines.Zip(got.Lines))
            Assert.Equal(
                (w.No, w.Name, w.SellerArticleId, w.Gtin, w.Quantity, w.UnitCode, w.UnitPrice, w.PriceBaseQty, w.LineNet, w.Vat),
                (g.No, g.Name, g.SellerArticleId, g.Gtin, g.Quantity, g.UnitCode, g.UnitPrice, g.PriceBaseQty, g.LineNet, g.Vat));
    }

    [Theory]
    [MemberData(nameof(EInvoices))]
    public void TheLinesOfARealEInvoiceAddUpToItsNetTotal(string relative)
    {
        var path = TestData.Fixture(relative);
        var inv = InvoiceParser.Parse(Path.GetFileName(path), File.ReadAllBytes(path));
        Assert.Equal(inv.NetTotal, InvoiceMath.LineTotals(inv.Lines).Net);
        Assert.All(inv.Lines, l => Assert.Equal(l.LineNet, InvoiceMath.LineNet(l.Quantity, l.UnitPrice, l.PriceBaseQty)));
    }
}

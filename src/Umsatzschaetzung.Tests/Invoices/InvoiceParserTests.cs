using System.Text;
using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using static Umsatzschaetzung.Tests.Invoices.Samples;

namespace Umsatzschaetzung.Tests.Invoices;

public class InvoiceParserTests
{
    static byte[] ZugferdPdf => File.ReadAllBytes(TestData.File("zugferd.pdf"));

    static byte[] PlainPdf => PdfFile(("/Length1 0", "BT /F1 12 Tf (Rechnung) Tj ET"u8.ToArray()));

    [Fact]
    public void AUblInvoiceIsDetected() => Assert.Equal(Kind.Ubl, InvoiceParser.Detect(Bytes(UblXml())));

    [Fact]
    public void ACiiInvoiceIsDetected() => Assert.Equal(Kind.Cii, InvoiceParser.Detect(Bytes(CiiXml())));

    [Fact]
    public void APdfWithAnEmbeddedCiiIsZugferd() => Assert.Equal(Kind.Zugferd, InvoiceParser.Detect(ZugferdPdf));

    [Fact]
    public void APdfWithoutOneIsAPlainPdf() => Assert.Equal(Kind.Pdf, InvoiceParser.Detect(PlainPdf));

    [Fact]
    public void APdfHeaderAfterLeadingJunkIsStillAPdf() =>
        Assert.Equal(Kind.Pdf, InvoiceParser.Detect([.. new byte[100], .. PlainPdf]));

    [Theory]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 })]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 16 })]
    [InlineData(new byte[] { (byte)'I', (byte)'I', (byte)'*', 0, 8, 0, 0, 0 })]
    [InlineData(new byte[] { (byte)'M', (byte)'M', 0, (byte)'*', 0, 0, 0, 8 })]
    public void PngJpegAndTiffAreImages(byte[] data) => Assert.Equal(Kind.Image, InvoiceParser.Detect(data));

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("<html><body>Rechnung</body></html>")]
    [InlineData("<?xml version=\"1.0\"?><CreditNote/>")]
    [InlineData("<Invoice")]
    [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16\"?><Invoice/>")]
    [InlineData("\u0089PNG")]
    public void AnythingElseIsUnknown(string text) => Assert.Equal(Kind.Unknown, InvoiceParser.Detect(Encoding.UTF8.GetBytes(text)));

    [Fact]
    public void AnXmlRootAfterAPrologIsFound() =>
        Assert.Equal(Kind.Ubl, InvoiceParser.Detect(Bytes("<?xml version=\"1.0\"?>\n<!-- XRechnung -->\n<?pi x?>\n<Invoice><broken></Invoice>")));

    [Fact]
    public void ParseStampsSourceAndFileName()
    {
        var ubl = InvoiceParser.Parse("a.xml", Bytes(UblXml()));
        Assert.Equal((Source.Ubl, "a.xml"), (ubl.Source, ubl.FileName));
        var cii = InvoiceParser.Parse("b.xml", Bytes(CiiXml()));
        Assert.Equal((Source.Cii, "b.xml"), (cii.Source, cii.FileName));
        var zugferd = InvoiceParser.Parse("c.pdf", ZugferdPdf);
        Assert.Equal((Source.Zugferd, "c.pdf"), (zugferd.Source, zugferd.FileName));
        Assert.Equal("", zugferd.Id);
    }

    [Fact]
    public void TheZugferdSampleParsesWithoutOcr()
    {
        var inv = InvoiceParser.Parse("zugferd.pdf", ZugferdPdf);
        Assert.Equal("RE-20201121/508", inv.Number);
        Assert.Equal("Bei Spiel GmbH", inv.SupplierName);
        Assert.Equal(new DateOnly(2020, 11, 21), inv.Date);
        Assert.Equal("EUR", inv.Currency);
        Assert.Equal(49_600, inv.NetTotal);
        Assert.Equal(57_104, inv.GrossTotal);
        Assert.Equal(3, inv.Lines.Count);
        var hot = inv.Lines[2];
        Assert.Equal((3L, "Hot air „heiße Luft“ (litres)", "LTR"), (hot.No, hot.Name, hot.UnitCode));
        Assert.Equal((800_000L, 25_000L, 1000L, 2000L, 1900L), (hot.Quantity, hot.UnitPrice, hot.PriceBaseQty, hot.LineNet, hot.Vat));
        Assert.Equal((inv.NetTotal, inv.GrossTotal), InvoiceMath.LineTotals(inv.Lines));
    }

    [Theory]
    [InlineData("scan.jpeg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 })]
    [InlineData("notes.txt", new byte[] { (byte)'h', (byte)'i' })]
    public void WhatIsNoEInvoiceIsRejectedWithTheFileName(string name, byte[] data)
    {
        var e = Assert.Throws<InvalidDataException>(() => InvoiceParser.Parse(name, data));
        Assert.Equal($"{name}: unknown invoice format", e.Message);
    }

    [Fact]
    public void APlainPdfIsRejected() =>
        Assert.Equal("scan.pdf: unknown invoice format", Assert.Throws<InvalidDataException>(() => InvoiceParser.Parse("scan.pdf", PlainPdf)).Message);

    [Fact]
    public void MalformedXmlIsInvalidDataNamingTheFile()
    {
        var xml = CiiXml();
        var e = Assert.Throws<InvalidDataException>(() => InvoiceParser.Parse("r.xml", Bytes(xml[..(xml.Length / 2)])));
        Assert.StartsWith("r.xml: cii: ", e.Message);
        Assert.IsType<InvalidDataException>(e.InnerException);
    }

    [Fact]
    public void ABadNumberIsInvalidDataNamingFileAndLine()
    {
        var e = Assert.Throws<InvalidDataException>(() => InvoiceParser.Parse("r.xml", Bytes(Replace(UblXml(), "InvoicedQuantity", "viele"))));
        Assert.Equal("r.xml: ubl: line 1: quantity: invalid number \"viele\"", e.Message);
    }
}

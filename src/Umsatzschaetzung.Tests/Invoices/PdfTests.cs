using Umsatzschaetzung.Invoices;
using static Umsatzschaetzung.Tests.Invoices.Samples;

namespace Umsatzschaetzung.Tests.Invoices;

public class PdfTests
{
    static readonly byte[] Xml = Bytes(CiiXml());

    static byte[]? Extract(byte[] pdf) => Pdf.TryExtractEmbeddedXml(pdf, out var xml) ? xml : null;

    [Fact]
    public void AFlateCompressedAttachmentIsInflated() =>
        Assert.Equal(Xml, Extract(PdfFile((EmbeddedFile + " /Filter /FlateDecode", Deflate(Xml)))));

    [Fact]
    public void AnUncompressedAttachmentIsTakenAsIs() => Assert.Equal(Xml, Extract(PdfFile((EmbeddedFile, Xml))));

    [Fact]
    public void OtherStreamsBeforeTheInvoiceAreSkipped()
    {
        var pdf = PdfFile(
            ("/Type /Metadata /Subtype /XML", Bytes("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>")),
            ("/Filter /FlateDecode", [1, 2, 3, 4]),
            ("/Subtype /Image /Width 1", Xml),
            ("/Type /Font", Xml),
            ("/Filter /FlateDecode", Deflate(Bytes(UblXml()))),
            (EmbeddedFile + " /Filter /FlateDecode", Deflate(Xml)));
        Assert.Equal(Xml, Extract(pdf));
    }

    [Fact]
    public void ImagesAndFontsAreNeverReadAsTheInvoice()
    {
        var pdf = PdfFile(("/Subtype /Image", Xml), ("/Type /FontFile2", Xml));
        Assert.Null(Extract(pdf));
    }

    [Fact]
    public void APdfWithoutAnInvoiceHasNone()
    {
        var pdf = PdfFile(("", "BT (x) Tj ET"u8.ToArray()));
        Assert.Null(Extract(pdf));
        var e = Assert.Throws<InvalidDataException>(() => Pdf.ExtractEmbeddedXml(pdf));
        Assert.Equal("pdf: no embedded CrossIndustryInvoice", e.Message);
    }

    [Fact]
    public void AStreamWithoutEndIsNotAnInvoice()
    {
        var pdf = PdfFile((EmbeddedFile, Xml));
        Assert.Null(Extract(pdf[..(pdf.Length / 2)]));
        Assert.Null(Extract("%PDF-1.7\n1 0 obj\n<< >>\nstream\n<rsm:CrossIndustryInvoice/>"u8.ToArray()));
    }

    [Fact]
    public void EmptyInputHasNoInvoice() => Assert.Null(Extract([]));

    [Theory]
    [InlineData((byte)'\n')]
    [InlineData((byte)'\r')]
    public void CompressedDataEndingInALineBreakByteSurvives(byte last)
    {
        byte[] xml = [], z = [];
        for (var i = 0; z.Length == 0 || z[^1] != last; i++)
        {
            xml = Bytes(CiiXml() + $"<!-- {i} -->");
            z = Deflate(xml);
        }
        var pdf = PdfFile((EmbeddedFile + " /Filter /FlateDecode", z));
        Assert.Equal(xml, Extract(pdf));
        Assert.Equal(Kind.Zugferd, InvoiceParser.Detect(pdf));
        Assert.Equal("R-2025/17", InvoiceParser.Parse("x.pdf", pdf).Number);
    }

    [Fact]
    public void TheSampleZugferdCarriesItsInvoice()
    {
        var xml = Pdf.ExtractEmbeddedXml(File.ReadAllBytes(TestData.File("zugferd.pdf")));
        Assert.Equal("RE-20201121/508", Cii.Parse(xml).Number);
    }
}

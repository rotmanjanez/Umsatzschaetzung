using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using static Umsatzschaetzung.Tests.Invoices.Samples;

namespace Umsatzschaetzung.Tests.Invoices;

public class CiiTests
{
    static Umsatzschaetzung.Model.Invoice Parse(string xml) => Cii.Parse(Bytes(xml));

    [Fact]
    public void TheHeaderIsReadFromTheSellerTheDocumentAndTheSettlement()
    {
        var inv = Parse(CiiXml());
        Assert.Equal(Source.Cii, inv.Source);
        Assert.Equal("Brauerei Bräu & Söhne", inv.SupplierName);
        Assert.Equal("R-2025/17", inv.Number);
        Assert.Equal(new DateOnly(2025, 1, 8), inv.Date);
        Assert.Equal("EUR", inv.Currency);
        Assert.Equal(2531, inv.NetTotal);
        Assert.Equal(3012, inv.GrossTotal);
        Assert.Null(inv.StatedNet);
        Assert.Null(inv.StatedGross);
    }

    [Fact]
    public void ALineCarriesEveryFieldInItsScale()
    {
        var l = Assert.Single(Parse(CiiXml()).Lines);
        Assert.Equal(7, l.No);
        Assert.Equal("Pils 0,5 l", l.Name);
        Assert.Equal("A-17", l.SellerArticleId);
        Assert.Equal("4006381333931", l.Gtin);
        Assert.Equal(20_500, l.Quantity);
        Assert.Equal("XBO", l.UnitCode);
        Assert.Equal(12_345_679, l.UnitPrice);
        Assert.Equal(10_000, l.PriceBaseQty);
        Assert.Equal(2531, l.LineNet);
        Assert.Equal(1900, l.Vat);
        Assert.Null(l.MappingId);
    }

    [Fact]
    public void LinesKeepDocumentOrderAndNumberThemselvesWhereTheIdIsMissing()
    {
        var second = CiiLine.Replace("<ram:LineID>7</ram:LineID>", "").Replace("Pils", "Weizen");
        var inv = Parse(CiiXml(CiiLine + second + CiiLine.Replace(">7<", ">0<")));
        Assert.Equal(["Pils 0,5 l", "Weizen 0,5 l", "Pils 0,5 l"], inv.Lines.Select(l => l.Name));
        Assert.Equal([7L, 2, 3], inv.Lines.Select(l => l.No));
    }

    [Fact]
    public void ADocumentWithoutLinesHasNone() => Assert.Empty(Parse(CiiXml("")).Lines);

    public static TheoryData<string> Variants => new()
    {
        Unprefixed(CiiXml()),
        Renamed(Renamed(Renamed(CiiXml(), "rsm", "a"), "ram", "b"), "udt", "c"),
        Defaulted(CiiXml(), "ram"),
        CiiXml().Replace("urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100", "urn:ferd:CrossIndustryDocument:invoice:1p0"),
        "﻿" + CiiXml(),
        "  \n" + CiiXml(),
        CiiXml().Replace("<?xml version=\"1.0\" encoding=\"UTF-8\"?>", ""),
        CiiXml().Replace("<ram:Name> Pils 0,5 l </ram:Name>", "<ram:Name><![CDATA[ Pils 0,5 l ]]></ram:Name>"),
        CiiXml().Replace("<ram:Name> Pils 0,5 l </ram:Name>", "<ram:Name> Pils <!-- Fass --> 0,5 l </ram:Name>"),
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void NamespacesAndPrefixesDoNotMatter(string xml)
    {
        var inv = Parse(xml);
        Assert.Equal("R-2025/17", inv.Number);
        Assert.Equal("Brauerei Bräu & Söhne", inv.SupplierName);
        Assert.Equal(new DateOnly(2025, 1, 8), inv.Date);
        Assert.Equal(3012, inv.GrossTotal);
        var l = Assert.Single(inv.Lines);
        Assert.Equal("4006381333931", l.Gtin);
        Assert.Equal(20_500, l.Quantity);
        Assert.Equal(1900, l.Vat);
    }

    [Fact]
    public void AMissingOptionalElementLeavesItsDefault()
    {
        var xml = CiiXml();
        foreach (var e in new[] { "GlobalID", "SellerAssignedID", "BasisQuantity", "ApplicableTradeTax", "IssueDateTime",
                     "SellerTradeParty", "InvoiceCurrencyCode", "SpecifiedTradeSettlementHeaderMonetarySummation" })
            xml = Without(xml, e);
        var inv = Parse(xml);
        Assert.Equal("", inv.SupplierName);
        Assert.Null(inv.Date);
        Assert.Equal("", inv.Currency);
        Assert.Equal(0, inv.NetTotal);
        Assert.Equal(0, inv.GrossTotal);
        var l = Assert.Single(inv.Lines);
        Assert.Null(l.Gtin);
        Assert.Null(l.SellerArticleId);
        Assert.Equal(1000, l.PriceBaseQty);
        Assert.Equal(0, l.Vat);
    }

    [Fact]
    public void BlankOptionalValuesAreAbsentToo()
    {
        var xml = Replace(Replace(Replace(CiiXml(), "GlobalID", " "), "SellerAssignedID", ""), "RateApplicablePercent", "");
        var l = Assert.Single(Parse(xml).Lines);
        Assert.Null(l.Gtin);
        Assert.Null(l.SellerArticleId);
        Assert.Equal(0, l.Vat);
    }

    [Fact]
    public void AMissingUnitCodeIsEmpty()
    {
        var l = Assert.Single(Parse(CiiXml().Replace(" unitCode=\" XBO \"", "")).Lines);
        Assert.Equal("", l.UnitCode);
        Assert.Equal(20_500, l.Quantity);
    }

    [Fact]
    public void TheFirstTaxOfALineGivesItsRate()
    {
        var two = CiiLine.Replace(
            "<ram:ApplicableTradeTax><ram:RateApplicablePercent>19.00</ram:RateApplicablePercent></ram:ApplicableTradeTax>",
            "<ram:ApplicableTradeTax><ram:RateApplicablePercent>7</ram:RateApplicablePercent></ram:ApplicableTradeTax>"
            + "<ram:ApplicableTradeTax><ram:RateApplicablePercent>19</ram:RateApplicablePercent></ram:ApplicableTradeTax>");
        Assert.Equal(700, Assert.Single(Parse(CiiXml(two)).Lines).Vat);
    }

    [Theory]
    [InlineData("<udt:DateTimeString format=\"102\">20250108</udt:DateTimeString>", 2025, 1, 8)]
    [InlineData("<udt:DateTimeString>20250108</udt:DateTimeString>", 2025, 1, 8)]
    [InlineData("<udt:DateTimeString>2025-01-08</udt:DateTimeString>", 2025, 1, 8)]
    [InlineData("<udt:DateTimeString format=\" 102 \">20250108</udt:DateTimeString>", 2025, 1, 8)]
    public void TheIssueDateIsReadInFormat102OrIso(string element, int y, int m, int d) =>
        Assert.Equal(new DateOnly(y, m, d), Parse(CiiXml().Replace("<udt:DateTimeString format=\"102\">20250108</udt:DateTimeString>", element)).Date);

    [Theory]
    [InlineData("<udt:DateTimeString format=\"610\">202501</udt:DateTimeString>")]
    [InlineData("<udt:DateTimeString format=\"102\">2025-01-08</udt:DateTimeString>")]
    [InlineData("<udt:DateTimeString format=\"102\">20251340</udt:DateTimeString>")]
    [InlineData("<udt:DateTimeString format=\"102\"></udt:DateTimeString>")]
    public void AnUnreadableIssueDateIsAbsentNotAnError(string element) =>
        Assert.Null(Parse(CiiXml().Replace("<udt:DateTimeString format=\"102\">20250108</udt:DateTimeString>", element)).Date);

    [Theory]
    [InlineData("BilledQuantity", "abc", "cii: line 1: quantity: invalid number \"abc\"")]
    [InlineData("BilledQuantity", "", "cii: line 1: quantity: empty number")]
    [InlineData("LineTotalAmount", "1.005", "cii: line 1: line net: number \"1.005\" has more than 2 decimals")]
    [InlineData("ChargeAmount", "1,5", "cii: line 1: price: invalid number \"1,5\"")]
    [InlineData("BasisQuantity", "x", "cii: line 1: base quantity: invalid number \"x\"")]
    [InlineData("RateApplicablePercent", "19%", "cii: line 1: vat: invalid number \"19%\"")]
    [InlineData("TaxBasisTotalAmount", "x", "cii: net total: invalid number \"x\"")]
    [InlineData("GrandTotalAmount", "x", "cii: gross total: invalid number \"x\"")]
    public void ABadNumberNamesWhereItStands(string element, string value, string message)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(Replace(CiiXml(), element, value)));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public void ARequiredLineAmountThatIsMissingIsAnError()
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(Without(CiiXml(), "SpecifiedTradeSettlementLineMonetarySummation")));
        Assert.Equal("cii: line 1: line net: empty number", e.Message);
        e = Assert.Throws<InvalidDataException>(() => Parse(Without(CiiXml(), "NetPriceProductTradePrice")));
        Assert.Equal("cii: line 1: price: empty number", e.Message);
    }

    [Fact]
    public void ADifferentRootIsRejected()
    {
        var e = Assert.Throws<InvalidDataException>(() => Cii.Parse(Bytes(UblXml())));
        Assert.Equal("cii: expected element type <CrossIndustryInvoice> but have <Invoice>", e.Message);
    }

    [Theory]
    [InlineData("<rsm:CrossIndustryInvoice xmlns:rsm=\"x\"><rsm:ExchangedDocument>")]
    [InlineData("<CrossIndustryInvoice><a></b></CrossIndustryInvoice>")]
    [InlineData("")]
    [InlineData("not xml at all")]
    public void MalformedXmlIsInvalidData(string xml)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(xml));
        Assert.StartsWith("cii: ", e.Message);
    }

    [Fact]
    public void AnUnsupportedCharsetIsInvalidData()
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(CiiXml().Replace("encoding=\"UTF-8\"", "encoding=\"EBCDIC\"")));
        Assert.Equal("cii: unsupported charset \"ebcdic\"", e.Message);
    }

    [Fact]
    public void ADocumentTypeDeclarationIsNotResolved()
    {
        var xml = CiiXml().Replace("<rsm:CrossIndustryInvoice ",
            "<!DOCTYPE rsm:CrossIndustryInvoice [<!ENTITY x SYSTEM \"file:///etc/passwd\">]>\n<rsm:CrossIndustryInvoice ");
        Assert.Equal("R-2025/17", Parse(xml).Number);
    }
}

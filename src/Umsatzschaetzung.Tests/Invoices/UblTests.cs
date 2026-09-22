using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using static Umsatzschaetzung.Tests.Invoices.Samples;

namespace Umsatzschaetzung.Tests.Invoices;

public class UblTests
{
    static Umsatzschaetzung.Model.Invoice Parse(string xml) => Ubl.Parse(Bytes(xml));

    [Fact]
    public void TheHeaderIsReadFromTheSupplierAndTheMonetaryTotal()
    {
        var inv = Parse(UblXml());
        Assert.Equal(Source.Ubl, inv.Source);
        Assert.Equal("Brauerei Bräu & Söhne", inv.SupplierName);
        Assert.Equal("R-2025/17", inv.Number);
        Assert.Equal(new DateOnly(2025, 1, 8), inv.Date);
        Assert.Equal("EUR", inv.Currency);
        Assert.Equal(2531, inv.NetTotal);
        Assert.Equal(3012, inv.GrossTotal);
    }

    [Fact]
    public void ALineCarriesEveryFieldInItsScale()
    {
        var l = Assert.Single(Parse(UblXml()).Lines);
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
    }

    [Fact]
    public void TheDocumentIdIsNotTakenFromALine()
    {
        var inv = Parse(UblXml().Replace("<cbc:ID> R-2025/17 </cbc:ID>", ""));
        Assert.Equal("", inv.Number);
        Assert.Equal(7, Assert.Single(inv.Lines).No);
    }

    [Fact]
    public void TheLegalNameStandsInForAMissingTradingName()
    {
        Assert.Equal("Brauerei Bräu GmbH", Parse(Without(UblXml(), "PartyName")).SupplierName);
        Assert.Equal("Brauerei Bräu GmbH", Parse(Replace(UblXml(), "Name", "  ")).SupplierName);
    }

    [Fact]
    public void TheCustomerIsNeverTheSupplier() =>
        Assert.Equal("", Parse(Without(UblXml(), "AccountingSupplierParty")).SupplierName);

    [Fact]
    public void LinesKeepDocumentOrderAndNumberThemselvesWhereTheIdIsMissing()
    {
        var second = UblLine.Replace("<cbc:ID>7</cbc:ID>", "").Replace("Pils", "Weizen");
        var inv = Parse(UblXml(UblLine + second));
        Assert.Equal(["Pils 0,5 l", "Weizen 0,5 l"], inv.Lines.Select(l => l.Name));
        Assert.Equal([7L, 2], inv.Lines.Select(l => l.No));
    }

    public static TheoryData<string> Variants => new()
    {
        Unprefixed(UblXml()),
        Renamed(Renamed(Renamed(UblXml(), "ubl", "inv"), "cac", "a"), "cbc", "b"),
        Defaulted(UblXml(), "ubl"),
        "﻿" + UblXml(),
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void NamespacesAndPrefixesDoNotMatter(string xml)
    {
        var inv = Parse(xml);
        Assert.Equal("R-2025/17", inv.Number);
        Assert.Equal("Brauerei Bräu & Söhne", inv.SupplierName);
        Assert.Equal(3012, inv.GrossTotal);
        var l = Assert.Single(inv.Lines);
        Assert.Equal("A-17", l.SellerArticleId);
        Assert.Equal(20_500, l.Quantity);
    }

    [Fact]
    public void AMissingOptionalElementLeavesItsDefault()
    {
        var xml = UblXml();
        foreach (var e in new[] { "SellersItemIdentification", "StandardItemIdentification", "ClassifiedTaxCategory", "BaseQuantity",
                     "IssueDate", "DocumentCurrencyCode", "LegalMonetaryTotal", "AccountingSupplierParty" })
            xml = Without(xml, e);
        var inv = Parse(xml);
        Assert.Equal("", inv.SupplierName);
        Assert.Null(inv.Date);
        Assert.Equal("", inv.Currency);
        Assert.Equal(0, inv.NetTotal);
        Assert.Equal(0, inv.GrossTotal);
        var l = Assert.Single(inv.Lines);
        Assert.Null(l.SellerArticleId);
        Assert.Null(l.Gtin);
        Assert.Equal(1000, l.PriceBaseQty);
        Assert.Equal(0, l.Vat);
    }

    [Theory]
    [InlineData("2025-1-8")]
    [InlineData("20250108")]
    [InlineData("")]
    public void AnUnreadableIssueDateIsAbsentNotAnError(string date) => Assert.Null(Parse(Replace(UblXml(), "IssueDate", date)).Date);

    [Theory]
    [InlineData("InvoicedQuantity", "x", "ubl: line 1: quantity: invalid number \"x\"")]
    [InlineData("PriceAmount", "", "ubl: line 1: price: empty number")]
    [InlineData("BaseQuantity", "1.0001", "ubl: line 1: base quantity: number \"1.0001\" has more than 3 decimals")]
    [InlineData("Percent", "neunzehn", "ubl: line 1: vat: invalid number \"neunzehn\"")]
    [InlineData("TaxExclusiveAmount", "x", "ubl: net total: invalid number \"x\"")]
    [InlineData("TaxInclusiveAmount", "x", "ubl: gross total: invalid number \"x\"")]
    public void ABadNumberNamesWhereItStands(string element, string value, string message)
    {
        var e = Assert.Throws<InvalidDataException>(() => Parse(Replace(UblXml(), element, value)));
        Assert.Equal(message, e.Message);
    }

    [Fact]
    public void ADifferentRootIsRejected()
    {
        var e = Assert.Throws<InvalidDataException>(() => Ubl.Parse(Bytes(CiiXml())));
        Assert.Equal("ubl: expected element type <Invoice> but have <CrossIndustryInvoice>", e.Message);
    }

    [Fact]
    public void MalformedXmlIsInvalidData()
    {
        var xml = UblXml();
        var e = Assert.Throws<InvalidDataException>(() => Parse(xml[..(xml.Length / 2)]));
        Assert.StartsWith("ubl: ", e.Message);
    }

    [Theory]
    [InlineData("ISO-8859-1")]
    [InlineData("latin1")]
    public void ALatin1DocumentKeepsItsUmlauts(string charset)
    {
        var xml = UblXml().Replace("encoding=\"UTF-8\"", $"encoding='{charset}'");
        var inv = Ubl.Parse(System.Text.Encoding.Latin1.GetBytes(xml));
        Assert.Equal("Brauerei Bräu & Söhne", inv.SupplierName);
    }

    [Fact]
    public void AWindows1252DocumentKeepsItsTypographicQuotesAndEuroSign()
    {
        var cp1252 = System.Text.CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
        var xml = UblXml().Replace("encoding=\"UTF-8\"", "encoding=\"windows-1252\"").Replace("Pils 0,5 l", "„Pils“ 0,5 l – 1 €");
        var l = Assert.Single(Ubl.Parse(cp1252.GetBytes(xml)).Lines);
        Assert.Equal("„Pils“ 0,5 l – 1 €", l.Name);
    }
}

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Umsatzschaetzung.Tests.Invoices;

static class Samples
{
    public const string CiiLine = """
        <ram:IncludedSupplyChainTradeLineItem>
          <ram:AssociatedDocumentLineDocument><ram:LineID>7</ram:LineID></ram:AssociatedDocumentLineDocument>
          <ram:SpecifiedTradeProduct>
            <ram:GlobalID schemeID="0160">4006381333931</ram:GlobalID>
            <ram:SellerAssignedID>A-17</ram:SellerAssignedID>
            <ram:Name> Pils 0,5 l </ram:Name>
          </ram:SpecifiedTradeProduct>
          <ram:SpecifiedLineTradeAgreement>
            <ram:NetPriceProductTradePrice>
              <ram:ChargeAmount>12.3456789</ram:ChargeAmount>
              <ram:BasisQuantity unitCode="XBO">10</ram:BasisQuantity>
            </ram:NetPriceProductTradePrice>
          </ram:SpecifiedLineTradeAgreement>
          <ram:SpecifiedLineTradeDelivery><ram:BilledQuantity unitCode=" XBO ">20.5</ram:BilledQuantity></ram:SpecifiedLineTradeDelivery>
          <ram:SpecifiedLineTradeSettlement>
            <ram:ApplicableTradeTax><ram:RateApplicablePercent>19.00</ram:RateApplicablePercent></ram:ApplicableTradeTax>
            <ram:SpecifiedTradeSettlementLineMonetarySummation><ram:LineTotalAmount>25.31</ram:LineTotalAmount></ram:SpecifiedTradeSettlementLineMonetarySummation>
          </ram:SpecifiedLineTradeSettlement>
        </ram:IncludedSupplyChainTradeLineItem>
        """;

    public static string CiiXml(string lines = CiiLine) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <rsm:CrossIndustryInvoice xmlns:rsm="urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100" xmlns:ram="urn:un:unece:uncefact:data:standard:ReusableAggregateBusinessInformationEntity:100" xmlns:udt="urn:un:unece:uncefact:data:standard:UnqualifiedDataType:100">
          <rsm:ExchangedDocument>
            <ram:ID> R-2025/17 </ram:ID>
            <ram:IssueDateTime><udt:DateTimeString format="102">20250108</udt:DateTimeString></ram:IssueDateTime>
          </rsm:ExchangedDocument>
          <rsm:SupplyChainTradeTransaction>
            {lines}
            <ram:ApplicableHeaderTradeAgreement>
              <ram:BuyerTradeParty><ram:Name>Gasthaus Zur Linde</ram:Name></ram:BuyerTradeParty>
              <ram:SellerTradeParty><ram:Name> Brauerei Bräu &amp; Söhne </ram:Name></ram:SellerTradeParty>
            </ram:ApplicableHeaderTradeAgreement>
            <ram:ApplicableHeaderTradeSettlement>
              <ram:InvoiceCurrencyCode>EUR</ram:InvoiceCurrencyCode>
              <ram:SpecifiedTradeSettlementHeaderMonetarySummation>
                <ram:TaxBasisTotalAmount>25.31</ram:TaxBasisTotalAmount>
                <ram:GrandTotalAmount>30.12</ram:GrandTotalAmount>
              </ram:SpecifiedTradeSettlementHeaderMonetarySummation>
            </ram:ApplicableHeaderTradeSettlement>
          </rsm:SupplyChainTradeTransaction>
        </rsm:CrossIndustryInvoice>
        """;

    public const string UblLine = """
        <cac:InvoiceLine>
          <cbc:ID>7</cbc:ID>
          <cbc:InvoicedQuantity unitCode="XBO">20.5</cbc:InvoicedQuantity>
          <cbc:LineExtensionAmount currencyID="EUR">25.31</cbc:LineExtensionAmount>
          <cac:Item>
            <cbc:Name> Pils 0,5 l </cbc:Name>
            <cac:SellersItemIdentification><cbc:ID>A-17</cbc:ID></cac:SellersItemIdentification>
            <cac:StandardItemIdentification><cbc:ID schemeID="0160">4006381333931</cbc:ID></cac:StandardItemIdentification>
            <cac:ClassifiedTaxCategory><cbc:ID>S</cbc:ID><cbc:Percent>19</cbc:Percent></cac:ClassifiedTaxCategory>
          </cac:Item>
          <cac:Price>
            <cbc:PriceAmount currencyID="EUR">12.3456789</cbc:PriceAmount>
            <cbc:BaseQuantity unitCode="XBO">10</cbc:BaseQuantity>
          </cac:Price>
        </cac:InvoiceLine>
        """;

    public static string UblXml(string lines = UblLine) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <ubl:Invoice xmlns:ubl="urn:oasis:names:specification:ubl:schema:xsd:Invoice-2" xmlns:cac="urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2" xmlns:cbc="urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2">
          <cbc:ID> R-2025/17 </cbc:ID>
          <cbc:IssueDate>2025-01-08</cbc:IssueDate>
          <cbc:DocumentCurrencyCode>EUR</cbc:DocumentCurrencyCode>
          <cac:AccountingSupplierParty>
            <cac:Party>
              <cac:PartyName><cbc:Name> Brauerei Bräu &amp; Söhne </cbc:Name></cac:PartyName>
              <cac:PartyLegalEntity><cbc:RegistrationName>Brauerei Bräu GmbH</cbc:RegistrationName></cac:PartyLegalEntity>
            </cac:Party>
          </cac:AccountingSupplierParty>
          <cac:AccountingCustomerParty>
            <cac:Party><cac:PartyName><cbc:Name>Gasthaus Zur Linde</cbc:Name></cac:PartyName></cac:Party>
          </cac:AccountingCustomerParty>
          <cac:LegalMonetaryTotal>
            <cbc:LineExtensionAmount currencyID="EUR">25.31</cbc:LineExtensionAmount>
            <cbc:TaxExclusiveAmount currencyID="EUR">25.31</cbc:TaxExclusiveAmount>
            <cbc:TaxInclusiveAmount currencyID="EUR">30.12</cbc:TaxInclusiveAmount>
            <cbc:PayableAmount currencyID="EUR">30.12</cbc:PayableAmount>
          </cac:LegalMonetaryTotal>
          {lines}
        </ubl:Invoice>
        """;

    public static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    public static string Without(string xml, string element) =>
        Regex.Replace(xml, $@"\s*<(\w+:)?{element}\b[^>]*?(/>|>.*?</(\w+:)?{element}>)", "", RegexOptions.Singleline);

    public static string Replace(string xml, string element, string text) =>
        Regex.Replace(xml, $@"(<(\w+:)?{element}\b[^>]*>).*?(</(\w+:)?{element}>)", m => m.Groups[1].Value + text + m.Groups[3].Value, RegexOptions.Singleline);

    public static string Unprefixed(string xml) =>
        Regex.Replace(Regex.Replace(xml, @"\s+xmlns:\w+=""[^""]*""", ""), @"(</?)\w+:", "$1");

    public static string Renamed(string xml, string from, string to) =>
        xml.Replace($"<{from}:", $"<{to}:").Replace($"</{from}:", $"</{to}:").Replace($"xmlns:{from}=", $"xmlns:{to}=");

    public static string Defaulted(string xml, string prefix) =>
        xml.Replace($"<{prefix}:", "<").Replace($"</{prefix}:", "</").Replace($"xmlns:{prefix}=", "xmlns=");

    public static byte[] PdfFile(params (string Dict, byte[] Data)[] streams)
    {
        using var pdf = new MemoryStream();
        void Write(string s) => pdf.Write(Encoding.Latin1.GetBytes(s));
        Write("%PDF-1.7\n%\xe2\xe3\xcf\xd3\n");
        var n = 1;
        foreach (var (dict, data) in streams)
        {
            Write($"{n++} 0 obj\n<< {dict} /Length {data.Length} >>\nstream\r\n");
            pdf.Write(data);
            Write("\r\nendstream\nendobj\n");
        }
        Write("trailer\n<< /Size 1 >>\n%%EOF\n");
        return pdf.ToArray();
    }

    public const string EmbeddedFile = "/Type /EmbeddedFile /Subtype /text#2Fxml";

    public static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Optimal))
            z.Write(data);
        return output.ToArray();
    }
}

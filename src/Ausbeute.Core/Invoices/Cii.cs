using System.Xml.Linq;
using Ausbeute.Model;

namespace Ausbeute.Invoices;

public static class Cii
{
    const string Transaction = "SupplyChainTradeTransaction";
    const string Settlement = Transaction + ">ApplicableHeaderTradeSettlement";

    public static Model.Invoice Parse(byte[] data)
    {
        XElement root;
        try
        {
            root = Xml.Load(data);
        }
        catch (Exception e) when (e is System.Xml.XmlException or InvalidDataException)
        {
            throw new InvalidDataException($"cii: {e.Message}", e);
        }
        if (root.Name.LocalName != InvoiceParser.RootCii)
            throw new InvalidDataException($"cii: expected element type <{InvoiceParser.RootCii}> but have <{root.Name.LocalName}>");
        var seller = Xml.Last(root, Transaction + ">ApplicableHeaderTradeAgreement>SellerTradeParty");
        var totals = Xml.Last(root, Settlement + ">SpecifiedTradeSettlementHeaderMonetarySummation");
        var inv = new Model.Invoice
        {
            Source = Source.Cii,
            SupplierName = seller is null ? "" : Xml.Text(seller, "Name").Trim(),
            SupplierVatId = Numbers.Optional(VatId(seller)),
            Number = Xml.Text(root, "ExchangedDocument>ID").Trim(),
            Currency = Xml.Text(root, Settlement + ">InvoiceCurrencyCode").Trim(),
        };
        inv.Date = Numbers.Wrap("cii", () => Date(Xml.Last(root, "ExchangedDocument>IssueDateTime>DateTimeString")));
        inv.NetTotal = Numbers.Wrap("cii: net total", () => Numbers.OptionalCents(totals is null ? "" : Xml.Text(totals, "TaxBasisTotalAmount")));
        inv.GrossTotal = Numbers.Wrap("cii: gross total", () => Numbers.OptionalCents(totals is null ? "" : Xml.Text(totals, "GrandTotalAmount")));
        var i = 0;
        foreach (var l in Xml.Path(root, Transaction + ">IncludedSupplyChainTradeLineItem"))
        {
            var index = i++;
            inv.Lines.Add(Numbers.Wrap($"cii: line {index + 1}", () => Line(l, index)));
        }
        return inv;
    }

    static DateOnly Date(XElement? dt)
    {
        var format = Xml.Attr(dt, "format").Trim();
        return format switch
        {
            "102" => Numbers.Date(Xml.Text(dt), "yyyyMMdd"),
            "" => Numbers.Date(Xml.Text(dt), "yyyyMMdd", "yyyy-MM-dd"),
            _ => throw new InvalidDataException($"unsupported date format \"{format}\""),
        };
    }

    static string VatId(XElement? seller)
    {
        if (seller is null)
            return "";
        foreach (var id in Xml.Path(seller, "SpecifiedTaxRegistration>ID"))
            if (string.Equals(Xml.Attr(id, "schemeID").Trim(), "VA", StringComparison.OrdinalIgnoreCase))
                return Xml.Text(id).Trim();
        return "";
    }

    static InvoiceLine Line(XElement l, int index)
    {
        var quantity = Xml.Last(l, "SpecifiedLineTradeDelivery>BilledQuantity");
        const string price = "SpecifiedLineTradeAgreement>NetPriceProductTradePrice";
        var line = new InvoiceLine
        {
            No = Numbers.LineNo(Xml.Text(l, "AssociatedDocumentLineDocument>LineID"), index),
            Name = Xml.Text(l, "SpecifiedTradeProduct>Name").Trim(),
            SellerArticleId = Numbers.Optional(Xml.Text(l, "SpecifiedTradeProduct>SellerAssignedID")),
            Gtin = Numbers.Optional(Xml.Text(l, "SpecifiedTradeProduct>GlobalID")),
            UnitCode = Xml.Attr(quantity, "unitCode").Trim(),
        };
        line.Quantity = Numbers.Wrap("quantity", () => Numbers.Milli(Xml.Text(quantity)));
        line.LineNet = Numbers.Wrap("line net", () => Numbers.Cents(Xml.Text(l, "SpecifiedLineTradeSettlement>SpecifiedTradeSettlementLineMonetarySummation>LineTotalAmount")));
        line.UnitPrice = Numbers.Wrap("price", () => Numbers.Micro(Xml.Text(l, price + ">ChargeAmount")));
        line.PriceBaseQty = Numbers.Wrap("base quantity", () => Numbers.BaseQty(Xml.Text(l, price + ">BasisQuantity")));
        var percents = Xml.Texts(l, "SpecifiedLineTradeSettlement>ApplicableTradeTax>RateApplicablePercent");
        if (percents.Count > 0)
            line.Vat = Numbers.Wrap("vat", () => Numbers.OptionalBp(percents[0]));
        return line;
    }
}

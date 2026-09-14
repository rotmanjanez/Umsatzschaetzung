using System.Xml.Linq;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Invoices;

public static class Ubl
{
    public static Model.Invoice Parse(byte[] data)
    {
        XElement root;
        try
        {
            root = Xml.Load(data);
        }
        catch (Exception e) when (e is System.Xml.XmlException or InvalidDataException)
        {
            throw new InvalidDataException($"ubl: {e.Message}", e);
        }
        if (root.Name.LocalName != InvoiceParser.RootUbl)
            throw new InvalidDataException($"ubl: expected element type <{InvoiceParser.RootUbl}> but have <{root.Name.LocalName}>");
        var supplier = Xml.Last(root, "AccountingSupplierParty");
        var totals = Xml.Last(root, "LegalMonetaryTotal");
        var inv = new Model.Invoice
        {
            Source = Source.Ubl,
            SupplierName = SupplierName(supplier),
            SupplierVatId = Numbers.Optional(VatId(supplier)),
            Number = Xml.Text(root, "ID").Trim(),
            Currency = Xml.Text(root, "DocumentCurrencyCode").Trim(),
        };
        try
        {
            inv.Date = Numbers.Date(Xml.Text(root, "IssueDate"), "yyyy-MM-dd");
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"ubl: {e.Message}", e);
        }
        inv.NetTotal = Numbers.Wrap("ubl: net total", () => Numbers.OptionalCents(totals is null ? "" : Xml.Text(totals, "TaxExclusiveAmount")));
        inv.GrossTotal = Numbers.Wrap("ubl: gross total", () => Numbers.OptionalCents(totals is null ? "" : Xml.Text(totals, "TaxInclusiveAmount")));
        var i = 0;
        foreach (var l in Xml.Path(root, "InvoiceLine"))
        {
            var index = i++;
            inv.Lines.Add(Numbers.Wrap($"ubl: line {index + 1}", () => Line(l, index)));
        }
        return inv;
    }

    static string SupplierName(XElement? supplier)
    {
        if (supplier is null)
            return "";
        foreach (var n in Xml.Texts(supplier, "Party>PartyName>Name"))
            if (n.Trim() is { Length: > 0 } t)
                return t;
        return Xml.Text(supplier, "Party>PartyLegalEntity>RegistrationName").Trim();
    }

    static string VatId(XElement? supplier)
    {
        if (supplier is null)
            return "";
        var schemes = Xml.Path(supplier, "Party>PartyTaxScheme").ToList();
        foreach (var s in schemes)
            if (string.Equals(Xml.Text(s, "TaxScheme>ID").Trim(), "VAT", StringComparison.OrdinalIgnoreCase))
                return Xml.Text(s, "CompanyID").Trim();
        foreach (var s in schemes)
            if (Xml.Text(s, "CompanyID").Trim() is { Length: > 0 } id)
                return id;
        return "";
    }

    static InvoiceLine Line(XElement l, int index)
    {
        var quantity = Xml.Last(l, "InvoicedQuantity");
        var line = new InvoiceLine
        {
            No = Numbers.LineNo(Xml.Text(l, "ID"), index),
            Name = Xml.Text(l, "Item>Name").Trim(),
            SellerArticleId = Numbers.Optional(Xml.Text(l, "Item>SellersItemIdentification>ID")),
            Gtin = Numbers.Optional(Xml.Text(l, "Item>StandardItemIdentification>ID")),
            UnitCode = Xml.Attr(quantity, "unitCode").Trim(),
        };
        line.Quantity = Numbers.Wrap("quantity", () => Numbers.Milli(Xml.Text(quantity)));
        line.LineNet = Numbers.Wrap("line net", () => Numbers.Cents(Xml.Text(l, "LineExtensionAmount")));
        line.UnitPrice = Numbers.Wrap("price", () => Numbers.Micro(Xml.Text(l, "Price>PriceAmount")));
        line.PriceBaseQty = Numbers.Wrap("base quantity", () => Numbers.BaseQty(Xml.Text(l, "Price>BaseQuantity")));
        var percents = Xml.Texts(l, "Item>ClassifiedTaxCategory>Percent");
        if (percents.Count > 0)
            line.Vat = Numbers.Wrap("vat", () => Numbers.OptionalBp(percents[0]));
        return line;
    }
}

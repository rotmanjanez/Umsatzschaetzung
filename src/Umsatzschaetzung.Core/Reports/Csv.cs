using System.Text;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Reports;

public static class Csv
{
    public static byte[] Invoice(Case c, Model.Invoice inv, RuleSet rs)
    {
        var b = new StringBuilder("﻿");
        void Row(params string[] cells)
        {
            for (var i = 0; i < cells.Length; i++)
            {
                if (i > 0) b.Append(';');
                b.Append(Quote(cells[i]));
            }
            b.Append("\r\n");
        }

        Row("Prüfung", c.Label);
        Row("Datei", inv.FileName);
        Row("Quelle", Display.SourceName(inv.Source));
        Row("Lieferant", inv.SupplierName);
        Row("Rechnungsnummer", inv.Number);
        Row("Datum", Format.Date(inv.Date));
        Row("Netto", Format.Cents(inv.NetTotal));
        Row("Brutto", Format.Cents(inv.GrossTotal));
        Row("Geprüft", Display.Verified(inv.Verification));
        Row("");

        Row("Zeile", "Position", "Artikelnummer", "GTIN", "Menge", "Einzelpreis", "Netto", "USt", "Zuordnung");
        foreach (var l in inv.Lines)
            Row(l.No.ToString(), l.Name, l.SellerArticleId ?? "", l.Gtin ?? "", Display.LineQuantity(l),
                Display.LineUnitPrice(l), Format.Cents(l.LineNet), Format.Bp(l.Vat), Display.MappingLabel(rs, l.MappingId));

        return Encoding.UTF8.GetBytes(b.ToString());
    }

    private static string Quote(string field)
    {
        if (field == "") return field;
        var needs = field == "\\." || field.AsSpan().IndexOfAny(";\"\r\n") >= 0 || char.IsWhiteSpace(field[0]);
        return needs ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }
}

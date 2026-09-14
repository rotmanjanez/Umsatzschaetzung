using System.Text;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Reports;

public static class Csv
{
    public static byte[] Render(Case c, Model.Report r, RuleSet rs)
    {
        var d = Display.Full(c, r, rs);
        var summary = Display.Summary(r);
        var revenue = Display.Revenue(c, r);
        var excluded = Display.Excluded(c, r, rs);

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
        void Blank() => Row("");

        Row("Umsatzschätzung", c.Label);
        Row("Zeitraum", d.Period);
        Row("Name des Steuerpflichtigen", c.Taxpayer.Name);
        Row("Steuernummer", c.Taxpayer.TaxNumber);
        Row("PaB-Nr.", c.Taxpayer.PabNumber);
        Row("Datum", d.Date);
        Row("Berechnet am", d.ComputedAt);
        Row("Regelwerk", d.RulesVersion);
        Blank();

        Row("Eingangspositionen");
        Row("Rechnungsnr.", "Datum", "Zeile", "Position", "Menge", "Einzelpreis", "Netto", "USt");
        foreach (var l in d.Lines)
            Row(l.Invoice, l.Date, l.LineNo.ToString(), l.Name, l.Quantity, l.UnitPrice, l.LineNet, l.Vat);
        Blank();

        Row("Umsätze vor und nach Betriebsprüfung (netto)");
        Row("Steuersatz", "Umsatz vor BP", "Umsatz nach BP", "Differenz");
        foreach (var v in revenue) Row(v.Vat, v.Declared, v.Calculated, v.Difference);
        Blank();

        Row("Zusammenfassung");
        Row("Kennzahl", "Wert");
        foreach (var it in summary) Row(it.Label, it.Value);
        Blank();

        Row("Produkte");
        Row("Produkt", "Rezeptur", "Portionen", "Fixiert", "Bruttopreis", "USt", "Umsatz netto", "Hinweis");
        foreach (var p in d.Products)
            Row(p.Name, p.Recipe, p.Portions, YesNo(p.Pinned), p.GrossPrice, p.Vat, p.Revenue, Display.ProductNote(p));
        Blank();

        Row("Zutaten");
        Row("Zutat", "Einheit", "Eingekauft", "Kosten", "Verbrauch", "Wareneinsatz", "Verkaufsfähig", "Rest");
        foreach (var i in r.Ingredients)
        {
            var unit = Display.IngredientUnit(rs, i.IngredientId);
            Row(Display.IngredientName(rs, i.IngredientId), Units.Code(unit), Format.Qty(i.Bought, unit), Format.Cents(i.Cost),
                Format.Qty(i.Used, unit), Format.Cents(i.UsedCost), Format.Qty(i.Sellable, unit), Format.Qty(i.Leftover, unit));
        }
        Blank();

        Row("Nicht berücksichtigte Positionen");
        Row("Rechnung", "Zeile", "Bezeichnung", "Zutat", "Grund", "Netto");
        foreach (var u in excluded.Unmapped) Row(u.Invoice, u.LineNo.ToString(), u.Name, "", "ohne Zuordnung", u.LineNet);
        foreach (var u in excluded.Unused) Row(u.Invoice, u.LineNo.ToString(), u.Name, u.Ingredient, "in keiner Rezeptur", u.LineNet);
        Blank();

        Row("Rechnungen");
        Row("Nummer", "Lieferant", "Datum", "Quelle", "Netto", "Brutto", "Positionen", "davon berücksichtigt", "Prüfung");
        foreach (var inv in d.Invoices)
            Row(inv.Number, inv.Supplier, inv.Date, inv.Source, inv.NetTotal, inv.GrossTotal, inv.Lines.ToString(), inv.Used.ToString(), inv.Verified);

        return Encoding.UTF8.GetBytes(b.ToString());
    }

    private static string Quote(string field)
    {
        if (field == "") return field;
        var needs = field == "\\." || field.AsSpan().IndexOfAny(";\"\r\n") >= 0 || char.IsWhiteSpace(field[0]);
        return needs ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }

    private static string YesNo(bool b) => b ? "ja" : "nein";
}

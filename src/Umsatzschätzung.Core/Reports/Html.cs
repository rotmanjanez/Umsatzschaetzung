using System.Text;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.Reports;

public static class Html
{
    static string Rechenweg()
    {
        using var s = typeof(Html).Assembly.GetManifestResourceStream("rechenweg.md")
            ?? throw new InvalidOperationException("rechenweg.md fehlt");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '&': b.Append("&amp;"); break;
                case '<': b.Append("&lt;"); break;
                case '>': b.Append("&gt;"); break;
                case '"': b.Append("&quot;"); break;
                case '\'': b.Append("&#39;"); break;
                default: b.Append(ch); break;
            }
        }
        return b.ToString();
    }

    private static string CssString(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\n' or '\r': b.Append("\\A "); break;
                default: b.Append(ch); break;
            }
        }
        return b.Append('"').ToString();
    }

    public static string Render(Case c, RuleSet rs, Model.Report r)
    {
        var f = Display.Full(c, r, rs);
        var d = Display.Report(c, r, rs);
        var tp = c.Taxpayer;
        var hasInventory = c.Inventory.Count > 0;
        var summary = d.Summary.ToDictionary(kv => kv.Key, kv => kv.Value);
        string Sum(string key) => Esc(summary.GetValueOrDefault(key) ?? "");
        var usedInvoices = f.Invoices.Where(i => i.Used > 0).ToList();
        var excludedCount = d.Excluded.Unmapped.Count + d.Excluded.Unused.Count;

        var b = new StringBuilder(64 * 1024);
        b.Append("<!DOCTYPE html>\n<html lang=\"de\">\n<head>\n<meta charset=\"utf-8\">\n");
        b.Append("<title>").Append(Esc("Umsatzschätzung " + c.Label)).Append("</title>\n");
        b.Append("<style>\n").Append(Css(c, f)).Append("</style>\n</head>\n<body>\n<main>\n");

        b.Append("<header class=\"cover\">\n<div class=\"cover-left\">\n");
        b.Append("<div class=\"note\">Umsatzschätzung</div>\n<hr class=\"light\">\n");
        b.Append("<div class=\"title\">").Append(Esc(c.Label)).Append("</div>\n");
        b.Append("<div>").Append(Esc(tp.Name)).Append("</div>\n");
        b.Append("<div>Zeitraum ").Append(Esc(f.Period)).Append("</div>\n</div>\n");
        b.Append("<dl class=\"cover-right\">\n");
        b.Append("<dt>Steuernummer</dt><dd>").Append(Esc(tp.TaxNumber)).Append("</dd>\n");
        b.Append("<dt>PaB-Nr.</dt><dd>").Append(Esc(tp.PabNumber)).Append("</dd>\n");
        b.Append("<dt>Datum</dt><dd>").Append(Esc(f.Date)).Append("</dd>\n");
        b.Append("<dt>Rechnungen</dt><dd>").Append(usedInvoices.Count).Append("</dd>\n");
        b.Append("</dl>\n</header>\n");

        b.Append("<p><strong>Umsatzschätzung ").Append(Esc(c.Label)).Append(", Zeitraum ").Append(Esc(f.Period)).Append("</strong></p>\n");
        b.Append("<p>Aus dem Wareneinkauf des Zeitraums ergibt sich der nachfolgend kalkulierte Umsatz. Abschnitt 1 führt die Eingangspositionen auf, Abschnitt 2 stellt die erklärten Umsätze dem kalkulierten Ergebnis gegenüber. Rezepturen, Warenfluss, Rechenweg und die Herleitung jedes Werts stehen im Anhang.</p>\n");

        b.Append("<section class=\"first\">\n<h1>1 Eingangspositionen</h1>\n");
        if (f.Lines.Count == 0)
        {
            b.Append("<p class=\"empty\">Keine Rechnungspositionen gehen in die Kalkulation ein.</p>\n");
        }
        else
        {
            b.Append("<table>\n<thead><tr><th>Rechnungsnr.</th><th>Datum</th><th class=\"wide\">Position</th><th class=\"num\">Menge</th><th class=\"num\">Einzelpreis</th><th class=\"num\">Netto</th></tr></thead>\n<tbody>\n");
            foreach (var l in f.Lines)
                Row(b, Esc(l.Invoice), Esc(l.Date), Esc(l.Name), Num(l.Quantity), Num(l.UnitPrice), Num(l.LineNet));
            b.Append("<tr class=\"total\"><td>Summe</td><td></td><td></td><td></td><td></td>").Append(Num(f.IncludedNet)).Append("</tr>\n");
            b.Append("</tbody>\n</table>\n");
            if (excludedCount > 0)
                b.Append("<p class=\"note\">Nicht berücksichtigt: ").Append(excludedCount).Append(" Positionen mit ").Append(Esc(d.Excluded.Excluded))
                    .Append(" (").Append(Esc(d.Excluded.Share)).Append(" der erfassten Einkäufe), weil sie keiner Zutat zugeordnet sind oder in keiner Rezeptur vorkommen.</p>\n");
        }
        b.Append("</section>\n");

        b.Append("<section>\n<h1>2 Umsätze vor und nach Betriebsprüfung</h1>\n");
        b.Append("<table>\n<thead><tr><th class=\"wide\">Steuersatz</th><th class=\"num\">Umsatz vor BP</th><th class=\"num\">Umsatz nach BP</th><th class=\"num\">Differenz</th></tr></thead>\n<tbody>\n");
        foreach (var v in d.Revenue.Where(v => !v.Total))
            Row(b, Esc(v.Vat), Num(v.Declared), Num(v.Calculated), Num(v.Difference));
        foreach (var v in d.Revenue.Where(v => v.Total))
            b.Append("<tr class=\"total\"><td>Summe</td>").Append(Num(v.Declared)).Append(Num(v.Calculated)).Append(Num(v.Difference)).Append("</tr>\n");
        b.Append("</tbody>\n</table>\n");
        b.Append("<p class=\"note\">Alle Beträge netto. Umsatz vor BP ist der erklärte Umsatz laut Angabe der Prüfung, Umsatz nach BP der aus dem Wareneinkauf kalkulierte Umsatz.</p>\n</section>\n");

        b.Append("<section>\n<h1>Anhang A: Rezepturen und Produkte</h1>\n");
        if (f.Products.Count == 0)
        {
            b.Append("<p class=\"empty\">Keine Produkte im Regelwerk.</p>\n");
        }
        else
        {
            b.Append("<table>\n<thead><tr><th>Produkt</th><th class=\"wide\">Rezeptur</th><th class=\"num\">Portionen</th><th class=\"num\">Bruttopreis</th><th class=\"num\">USt</th><th class=\"num\">Umsatz netto</th><th>Bemerkung</th></tr></thead>\n<tbody>\n");
            foreach (var p in f.Products)
            {
                var remark = p.Disabled ? "deaktiviert" : p.PriceMissing ? "Preis fehlt" : p.Pinned ? "vorgegeben" : "";
                Row(b, Esc(p.Name), Esc(p.Recipe), Num(p.Count), Num(p.GrossPrice), Num(p.Vat), Num(p.Revenue), Esc(remark));
            }
            b.Append("<tr class=\"total\"><td>Summe</td><td></td>").Append(Num(f.PortionTotal)).Append("<td></td><td></td>").Append(Num(summary.GetValueOrDefault("calculatedRevenueNet") ?? "")).Append("<td></td></tr>\n");
            b.Append("</tbody>\n</table>\n");
            b.Append("<p class=\"note\">Preise und Umsatzsteuersätze sind Angaben der Prüfung. Deaktivierte Produkte gehen nicht in die Kalkulation ein.</p>\n");
        }
        b.Append("</section>\n");

        b.Append("<section>\n<h1>Anhang B: Warenfluss und Ausbeute</h1>\n");
        if (f.Ingredients.Count == 0)
        {
            b.Append("<p class=\"empty\">Keine Zutaten im Regelwerk.</p>\n");
        }
        else if (hasInventory)
        {
            b.Append("<table>\n<thead><tr><th class=\"wide\">Zutat</th><th class=\"num\">Anfangsbestand</th><th class=\"num\">Einkauf</th><th class=\"num\">Endbestand</th><th class=\"num\">Verbrauch</th><th class=\"num\">Wareneinsatz</th><th class=\"num\">Ausbeute</th><th class=\"num\">Verkaufsfähig</th><th class=\"num\">Rest</th></tr></thead>\n<tbody>\n");
            foreach (var i in f.Ingredients)
                Row(b, Flag(i.Name, i.Binding), Num(i.Opening), Num(i.Bought), Num(i.Closing), Num(i.Used), Num(i.UsedCost), Num(i.Yield), Num(i.Sellable), Num(i.Leftover));
            b.Append("</tbody>\n</table>\n");
        }
        else
        {
            b.Append("<table>\n<thead><tr><th class=\"wide\">Zutat</th><th class=\"num\">Einkauf</th><th class=\"num\">Wareneinsatz</th><th class=\"num\">Ausbeute</th><th class=\"num\">Verkaufsfähig</th><th class=\"num\">Rest</th></tr></thead>\n<tbody>\n");
            foreach (var i in f.Ingredients)
                Row(b, Flag(i.Name, i.Binding), Num(i.Bought), Num(i.UsedCost), Num(i.Yield), Num(i.Sellable), Num(i.Leftover));
            b.Append("</tbody>\n</table>\n");
        }
        if (f.Ingredients.Any(i => i.Binding))
            b.Append("<p class=\"note\">* Diese Zutat ist vollständig verbraucht und begrenzt die Portionszahl.</p>\n");
        if (f.Allocations.Any(a => a.Approximate))
            b.Append("<p class=\"note\">Die Zuteilung wurde für einen Teil der Zutaten näherungsweise bestimmt.</p>\n");
        if (hasInventory)
            b.Append("<p class=\"note\">Der Wareneinsatz bewertet den Verbrauch mit dem durchschnittlichen Einkaufspreis des Zeitraums; die Differenz zu den Einkaufskosten ist die Bestandsveränderung.</p>\n");

        b.Append("<h2>Ertragssätze</h2>\n");
        if (f.YieldRates.Count == 0)
        {
            b.Append("<p class=\"empty\">Keine Ertragssätze hinterlegt.</p>\n");
        }
        else
        {
            b.Append("<table>\n<thead><tr><th class=\"wide\">Zutat und Grundlage</th><th class=\"num\">Schwund</th><th class=\"num\">Eigenverbrauch</th><th class=\"num\">Personal</th><th class=\"num\">Freiabgabe</th><th class=\"num\">Ausbeute</th></tr></thead>\n<tbody>\n");
            foreach (var y in f.YieldRates)
            {
                var basis = (y.Chosen ? "In der Prüfung gewählt. " : "") + y.Source;
                Row(b, Esc(y.Ingredient) + "<br><span class=\"note\">" + Esc(basis) + "</span>", Num(y.Shrinkage), Num(y.OwnUse), Num(y.Staff), Num(y.Free), Num(y.Yield));
            }
            b.Append("</tbody>\n</table>\n");
            b.Append("<p class=\"note\">Schwund umfasst bei Getränken den Schankverlust. Ohne Wahl in der Prüfung gilt der hinterlegte Standardsatz.</p>\n");
        }
        b.Append("</section>\n");

        b.Append("<section>\n<h1>Anhang C: Kennzahlen</h1>\n<table class=\"ledger\">\n<tbody>\n");
        Ledger(b, "Kalkulierter Umsatz (netto)", Sum("calculatedRevenueNet"), "");
        Ledger(b, "abzüglich Wareneinsatz", Sum("costOfGoods"), "");
        Ledger(b, "Rohgewinn", Sum("grossProfit"), "total");
        Ledger(b, "Rohgewinnaufschlag", Sum("markup"), "");
        Ledger(b, "Portionen gesamt", Sum("portions"), "");
        Ledger(b, "Erfasste Einkäufe (netto)", Sum("purchases"), "");
        if (hasInventory) Ledger(b, "Bestandsveränderung", Sum("stockChange"), "");
        Ledger(b, "Nicht berücksichtigt", Sum("excluded"), "");
        b.Append("</tbody>\n</table>\n</section>\n");

        b.Append("<section class=\"prose\">\n<h1>Anhang D: Rechenweg</h1>\n");
        b.Append(Markdown.ToHtml(Rechenweg(), 1));
        b.Append("</section>\n");

        b.Append("<section>\n<h1>Anhang E: Herleitung der Werte</h1>\n");
        b.Append("<p>Jede Zeile zeigt einen Wert mit seiner Rechnung; eingerückte Zeilen sind die Bestandteile der Zeile darüber.</p>\n");
        b.Append("<table class=\"tree\">\n<thead><tr><th class=\"wide\">Wert und Rechnung</th><th class=\"num\">Ergebnis</th></tr></thead>\n<tbody>\n");
        TreeRows(b, d.Root, 0);
        b.Append("</tbody>\n</table>\n</section>\n");

        b.Append("<section>\n<h1>Anhang F: Vorgaben der Prüfung</h1>\n<h2>Rechnungen</h2>\n");
        if (usedInvoices.Count == 0)
        {
            b.Append("<p class=\"empty\">Keine Rechnungen mit berücksichtigten Positionen.</p>\n");
        }
        else
        {
            b.Append("<table>\n<thead><tr><th>Nummer</th><th class=\"wide\">Lieferant</th><th>Datum</th><th class=\"num\">Positionen</th><th class=\"num\">Netto</th></tr></thead>\n<tbody>\n");
            foreach (var i in usedInvoices)
            {
                var number = Esc(i.Number) + (i.Verified != "" ? "<br><span class=\"note\">" + Esc(i.Verified) + "</span>" : "");
                Row(b, number, Esc(i.Supplier), Esc(i.Date), Num(i.Used.ToString()), Num(i.UsedNet));
            }
            b.Append("<tr class=\"total\"><td>Summe</td><td></td><td></td><td></td>").Append(Num(f.IncludedNet)).Append("</tr>\n");
            b.Append("</tbody>\n</table>\n");
        }
        b.Append("<h2>Vorgegebene Portionen</h2>\n");
        if (f.Pinned.Count == 0)
        {
            b.Append("<p>Keine Portionszahlen vorgegeben.</p>\n");
        }
        else
        {
            b.Append("<table>\n<thead><tr><th class=\"wide\">Produkt</th><th class=\"num\">Portionen</th><th class=\"wide\">Begründung</th></tr></thead>\n<tbody>\n");
            foreach (var p in f.Pinned) Row(b, Esc(p.Product), Num(p.Portions), Esc(p.Reason));
            b.Append("</tbody>\n</table>\n");
        }
        b.Append("<h2>Regelwerk</h2>\n");
        b.Append("<p>Berechnet am ").Append(Esc(f.ComputedAt)).Append(" mit Regelwerk Version ").Append(Esc(f.RulesVersion)).Append(".</p>\n</section>\n");

        if (f.Warnings.Count > 0)
        {
            b.Append("<section>\n<h1>Anhang G: Hinweise</h1>\n<ol class=\"warnings\">\n");
            foreach (var w in f.Warnings) b.Append("<li>").Append(Esc(w)).Append("</li>\n");
            b.Append("</ol>\n</section>\n");
        }

        b.Append("<footer class=\"stamp\">\n");
        Stamp(b, "Steuernummer", tp.TaxNumber);
        Stamp(b, "Name des Steuerpflichtigen", tp.Name);
        Stamp(b, "PaB-Nr.", tp.PabNumber);
        Stamp(b, "Datum", f.Date);
        b.Append("</footer>\n</main>\n</body>\n</html>\n");
        return b.ToString();
    }

    private static void Row(StringBuilder b, params string[] cells)
    {
        b.Append("<tr>");
        foreach (var cell in cells)
            b.Append(cell.StartsWith("<td") ? cell : "<td>" + cell + "</td>");
        b.Append("</tr>\n");
    }

    private static string Num(string s) => "<td class=\"num\">" + Esc(s) + "</td>";

    private static string Flag(string name, bool mark) => Esc(name) + (mark ? "<sup>*</sup>" : "");

    private static void Ledger(StringBuilder b, string label, string value, string cls)
    {
        b.Append(cls == "" ? "<tr>" : "<tr class=\"" + cls + "\">");
        b.Append("<td>").Append(Esc(label)).Append("</td><td class=\"num\">").Append(value).Append("</td></tr>\n");
    }

    private static void TreeRows(StringBuilder b, NodeDisplay n, int depth)
    {
        var tenths = depth * 14;
        b.Append("<tr><td style=\"padding-left:").Append(tenths / 10).Append('.').Append(tenths % 10).Append("em\">");
        b.Append(depth <= 1 ? "<strong>" + Esc(n.Label) + "</strong>" : Esc(n.Label));
        if (!string.IsNullOrEmpty(n.Formula)) b.Append("<br><span class=\"note\">").Append(Esc(n.Formula)).Append("</span>");
        b.Append("</td><td class=\"num\">").Append(Esc(n.Value)).Append("</td></tr>\n");
        foreach (var child in n.Inputs) TreeRows(b, child, depth + 1);
    }

    private static void Stamp(StringBuilder b, string label, string value) =>
        b.Append("<div><span class=\"note\">").Append(Esc(label)).Append("</span><br>").Append(Esc(value)).Append("</div>\n");

    private static string Css(Case c, FullDisplay f)
    {
        var tp = c.Taxpayer;
        var footer = CssString($"Steuernummer {tp.TaxNumber}   ·   Name des Steuerpflichtigen {tp.Name}   ·   PaB-Nr. {tp.PabNumber}   ·   Datum {f.Date}");
        var header = CssString("Umsatzschätzung · " + c.Label);
        var period = CssString(f.Period);
        return $$"""
            :root { --ink: #141414; --muted: #646464; --hairline: #bebebe; }
            html { color-scheme: light; }
            body { margin: 0; background: #f2f2f0; color: var(--ink); font-family: "Libertinus Serif", "Linux Libertine", Georgia, "Times New Roman", serif; font-size: 9.5pt; line-height: 1.45; }
            main { max-width: 165mm; margin: 0 auto; padding: 20mm 20mm 24mm 25mm; background: #fff; }
            p { margin: 0 0 0.6em; text-align: justify; hyphens: auto; }
            h1 { font-size: 10.5pt; font-weight: bold; margin: 1.8em 0 0.7em; }
            h2 { font-size: 9.5pt; font-weight: bold; margin: 1.2em 0 0.5em; }
            hr.light { border: 0; border-top: 0.3pt solid var(--hairline); margin: 0.2em 0 0.4em; width: 85mm; }
            .note { font-size: 8pt; color: var(--muted); }
            .empty { color: var(--muted); font-style: italic; }
            .title { font-size: 11pt; font-weight: bold; }
            .cover { display: flex; gap: 8mm; min-height: 78.5mm; padding-top: 25mm; box-sizing: border-box; }
            .cover-left { width: 100mm; }
            .cover-right { display: grid; grid-template-columns: auto 1fr; column-gap: 0.8em; row-gap: 0.5em; margin: 0; font-size: 9pt; align-content: start; }
            .cover-right dt { font-size: 8pt; color: var(--muted); }
            .cover-right dd { margin: 0; }
            table { width: 100%; border-collapse: collapse; font-size: 9pt; font-variant-numeric: tabular-nums; margin: 0.4em 0 0.6em; }
            th, td { padding: 3pt 5pt; text-align: left; vertical-align: top; border-bottom: 0.3pt solid var(--hairline); }
            th { font-size: 8pt; font-weight: bold; border-bottom: 0.7pt solid var(--ink); }
            th.wide { width: 40%; }
            td.num, th.num { text-align: right; white-space: nowrap; }
            tr.total td { font-weight: bold; border-top: 0.7pt solid var(--ink); }
            table.ledger { width: auto; min-width: 70mm; }
            table.ledger td:first-child { padding-right: 4em; }
            table.tree td { border-bottom: 0.3pt solid var(--hairline); }
            .prose pre { font-family: "Libertinus Mono", "DejaVu Sans Mono", Consolas, monospace; font-size: 8.5pt; margin: 0.4em 0 0.8em 2em; white-space: pre-wrap; }
            .prose code { font-family: "Libertinus Mono", "DejaVu Sans Mono", Consolas, monospace; font-size: 8.5pt; }
            .prose ul { padding-left: 1.5em; margin: 0 0 0.6em; }
            .prose li { margin-bottom: 0.3em; }
            ol.warnings { padding-left: 2em; }
            ol.warnings li { margin-bottom: 0.3em; }
            .stamp { display: flex; gap: 2.2em; margin-top: 3em; padding-top: 0.4em; border-top: 0.3pt solid var(--hairline); font-size: 8pt; }
            @page {
              size: A4;
              margin: 20mm 20mm 24mm 25mm;
              @top-left { content: {{header}}; font-size: 8pt; color: #646464; font-family: "Libertinus Serif", "Linux Libertine", Georgia, serif; }
              @top-right { content: {{period}}; font-size: 8pt; color: #646464; font-family: "Libertinus Serif", "Linux Libertine", Georgia, serif; }
              @bottom-left { content: {{footer}}; font-size: 8pt; white-space: pre; font-family: "Libertinus Serif", "Linux Libertine", Georgia, serif; }
              @bottom-right { content: "Seite " counter(page) " von " counter(pages); font-size: 8pt; font-family: "Libertinus Serif", "Linux Libertine", Georgia, serif; }
            }
            @page :first {
              @top-left { content: none; }
              @top-right { content: none; }
            }
            @media print {
              body { background: #fff; }
              main { max-width: none; margin: 0; padding: 0; }
              section { break-before: page; }
              section.first { break-before: auto; }
              thead { display: table-header-group; }
              tr { break-inside: avoid; }
              h1, h2 { break-after: avoid; }
              .stamp { display: none; }
            }

            """;
    }
}

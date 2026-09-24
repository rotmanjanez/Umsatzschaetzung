using System.Globalization;
using System.Text;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Reports;

public sealed record AssortmentImport(List<CaseProduct> Products, List<string> Unknown);

public static class Csv
{
    const string ProductColumn = "Produkt", PriceColumn = "Bruttopreis", VatColumn = "USt", IdColumn = "Produkt-ID";
    const long DefaultVat = 1900;

    public static byte[] Invoice(Case c, Model.Invoice inv, RuleSet rs)
    {
        var b = new StringBuilder("﻿");
        void Row(params string[] cells) => Append(b, cells);

        Row("Prüfung", c.Label);
        Row("Datei", inv.FileName);
        Row("Quelle", Format.Source(inv.Source));
        Row("Lieferant", inv.SupplierName);
        Row("Rechnungsnummer", inv.Number);
        Row("Datum", Format.Date(inv.Date));
        Row("Netto", Format.Cents(inv.NetTotal));
        Row("Brutto", Format.Cents(inv.GrossTotal));
        Row("Durchsicht", inv.Verification is { } v ? Format.Verified(v.At, v.Auto) : "");
        Row("");

        Row("Zeile", "Position", "Artikelnummer", "GTIN", "Menge", "Einzelpreis", "Netto", "USt", "Zuordnung");
        foreach (var l in inv.Lines)
            Row(l.No.ToString(), l.Name, l.SellerArticleId ?? "", l.Gtin ?? "", Format.Quantity(l.Quantity, l.UnitCode),
                Format.UnitPrice(l.UnitPrice, l.PriceBaseQty, l.UnitCode), Format.Cents(l.LineNet), Format.Bp(l.Vat),
                Names.Mapping(rs, l.MappingId));

        return Encoding.UTF8.GetBytes(b.ToString());
    }

    public static byte[] Assortment(Case c, RuleSet rs)
    {
        var b = new StringBuilder("﻿");
        Append(b, ProductColumn, PriceColumn, VatColumn, IdColumn);
        foreach (var (p, name) in c.Products
                     .Select(p => (p, Names.Product(rs, p.ProductId)))
                     .OrderBy(x => x.Item2, StringComparer.CurrentCulture))
            Append(b, name, p.GrossPrice > 0 ? Format.Cents(p.GrossPrice) : "", Format.Bp(p.Vat), p.ProductId);
        return Encoding.UTF8.GetBytes(b.ToString());
    }

    public static AssortmentImport ReadAssortment(byte[] data, RuleSet rs)
    {
        var rows = Parse(Decode(data));
        if (rows.Count == 0) throw new InvalidDataException("Die Datei ist leer.");
        var header = rows[0].Select(h => h.Trim()).ToList();
        int Column(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        int nameAt = Column(ProductColumn), priceAt = Column(PriceColumn), vatAt = Column(VatColumn), idAt = Column(IdColumn);
        if (nameAt < 0 && idAt < 0)
            throw new InvalidDataException($"Die Datei braucht eine Spalte \"{ProductColumn}\" oder \"{IdColumn}\".");

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in rs.Products.Values) byName.TryAdd(p.Name.Trim(), p.Id);

        var products = new Dictionary<string, CaseProduct>();
        List<string> unknown = [];
        for (var n = 1; n < rows.Count; n++)
        {
            var row = rows[n];
            string Cell(int at) => at >= 0 && at < row.Count ? row[at].Trim() : "";
            if (row.TrueForAll(string.IsNullOrWhiteSpace)) continue;
            var (id, name) = (Cell(idAt), Cell(nameAt));
            if (!rs.Products.ContainsKey(id) && !byName.TryGetValue(name, out id))
            {
                unknown.Add(name != "" ? name : Cell(idAt));
                continue;
            }
            products[id] = new CaseProduct
            {
                ProductId = id,
                GrossPrice = Hundredths(Cell(priceAt), n, PriceColumn) ?? 0,
                Vat = Hundredths(Cell(vatAt), n, VatColumn) ?? DefaultVat,
            };
        }
        return new AssortmentImport([.. products.Values], unknown);
    }

    static string Decode(byte[] data)
    {
        try
        {
            return new UTF8Encoding(false, true).GetString(data).TrimStart('\uFEFF');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(data);
        }
    }

    static List<List<string>> Parse(string text)
    {
        var eol = text.IndexOfAny(['\r', '\n']);
        var header = eol < 0 ? text : text[..eol];
        var sep = new[] { ';', ',', '\t' }.MaxBy(c => header.Count(x => x == c));
        List<List<string>> rows = [];
        List<string> row = [];
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch != '"') cell.Append(ch);
                else if (i + 1 < text.Length && text[i + 1] == '"') cell.Append(text[++i]);
                else quoted = false;
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep)
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (ch is '\r' or '\n')
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(cell.ToString());
                cell.Clear();
                rows.Add(row);
                row = [];
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }
        return rows;
    }

    // The last separator followed by one or two digits is the decimal mark, so both 1.234,5 and 1234.50 read.
    static long? Hundredths(string cell, int line, string column)
    {
        var s = new string([.. cell.Where(c => c is not ('€' or '%') && !char.IsWhiteSpace(c))]);
        if (s == "") return null;
        if (!s.All(c => char.IsAsciiDigit(c) || c is ',' or '.'))
            throw new InvalidDataException($"Zeile {line + 1}: {column} \"{cell}\" ist keine Zahl.");
        var mark = s.LastIndexOfAny([',', '.']);
        var hasDecimals = mark >= 0 && s.Length - mark - 1 is 1 or 2;
        var whole = (hasDecimals ? s[..mark] : s).Replace(",", "").Replace(".", "");
        var frac = hasDecimals ? s[(mark + 1)..].PadRight(2, '0') : "00";
        if (!long.TryParse(whole + frac, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            throw new InvalidDataException($"Zeile {line + 1}: {column} \"{cell}\" ist keine Zahl.");
        return v;
    }

    static void Append(StringBuilder b, params string[] cells)
    {
        for (var i = 0; i < cells.Length; i++)
        {
            if (i > 0) b.Append(';');
            b.Append(Quote(cells[i]));
        }
        b.Append("\r\n");
    }

    private static string Quote(string field)
    {
        if (field == "") return field;
        var needs = field == "\\." || field.AsSpan().IndexOfAny(";\"\r\n") >= 0 || char.IsWhiteSpace(field[0]);
        return needs ? "\"" + field.Replace("\"", "\"\"") + "\"" : field;
    }
}

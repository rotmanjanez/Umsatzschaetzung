namespace Umsatzschaetzung.Model;

// Eine Gruppe hält genau die Positionen, die eine Zuordnung abdeckt: Lieferant, der Schlüssel,
// nach dem Match eine Regel wählt, und die Einheit.
public static class LineKey
{
    public static string Of(string? supplier, InvoiceLine l) =>
        supplier + "|" + Identity(supplier, l) + "|" + l.UnitCode.ToUpperInvariant();

    static string Identity(string? supplier, InvoiceLine l) =>
        !string.IsNullOrEmpty(supplier) && !string.IsNullOrEmpty(l.SellerArticleId) ? "a:" + l.SellerArticleId
        : !string.IsNullOrEmpty(l.Gtin) ? "g:" + l.Gtin
        : "n:" + ArticleName.Canonical(l.Name);
}

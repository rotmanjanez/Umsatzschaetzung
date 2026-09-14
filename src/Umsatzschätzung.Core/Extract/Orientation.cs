using Umsatzschätzung.Model;

namespace Umsatzschätzung.Extract;

public static class Orientation
{
    public const int Confident = 8; // this number is arbitrarily choosen

    static readonly string[] Keywords =
    [
        "rechnung", "lieferschein", "summe", "netto", "brutto", "mwst", "ust", "gesamt", "betrag",
        "datum", "menge", "preis", "artikel", "position", "kunde", "lieferung", "zahlung", "steuer",
    ];

    public static int Score(IEnumerable<OcrWord> words)
    {
        var score = 0;
        foreach (var w in words)
        {
            var t = w.Text.Trim();
            if (t == "") continue;
            var lower = t.ToLowerInvariant();
            if (Keywords.Any(lower.Contains)) score += 3;
            else if (t.Contains('€') || lower == "eur" || t == "%") score += 2;
            else if (Parse.TryParseNumber(t, out _, out var scale)) score += scale == 2 ? 2 : 1;
        }
        return score;
    }
}

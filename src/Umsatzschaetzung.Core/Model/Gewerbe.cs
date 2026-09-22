using System.Text.RegularExpressions;

namespace Umsatzschaetzung.Model;

// Gewerbekennzahl der Richtsatzsammlung: "56101.0", oder ein Ziffernpräfix davon.
public static partial class Gewerbe
{
    public static bool Kennzahl(string s) => Pattern().IsMatch(s);

    // Beherbergung und Gastronomie trennen den Aufschlag nach Sparten; jedes andere Gewerbe
    // hat einen Satz für den ganzen Warenverkauf. Ohne Kennzahl bleibt es bei den Sparten.
    public static bool Gastronomie(string? kennzahl) =>
        string.IsNullOrEmpty(kennzahl) || kennzahl.StartsWith("55", StringComparison.Ordinal)
        || kennzahl.StartsWith("56", StringComparison.Ordinal);

    [GeneratedRegex(@"^\d{1,5}(\d\.\d)?$")] private static partial Regex Pattern();
}

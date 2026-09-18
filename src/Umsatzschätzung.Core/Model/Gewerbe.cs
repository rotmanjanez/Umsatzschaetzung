using System.Text.RegularExpressions;

namespace Umsatzschätzung.Model;

// Gewerbekennzahl der Richtsatzsammlung: "56101.0", oder ein Ziffernpräfix davon.
public static partial class Gewerbe
{
    public static bool Kennzahl(string s) => Pattern().IsMatch(s);

    [GeneratedRegex(@"^\d{1,5}(\d\.\d)?$")] private static partial Regex Pattern();
}

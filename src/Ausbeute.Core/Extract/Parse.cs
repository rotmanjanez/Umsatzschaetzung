using System.Globalization;
using Ausbeute.Model;

namespace Ausbeute.Extract;

public static class Parse
{
    const int MaxUnitLen = 8;

    public const int ScaleMilli = 3;
    public const int ScaleMicro = 6;
    public const int ScaleCents = 2;
    public const int ScaleBp = 2;

    static readonly string[] DateLayouts = ["dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "d.M.yy", "yyyy-MM-dd"];

    public static bool TryParseNumber(string input, out long value, out int scale)
    {
        value = 0;
        scale = 0;
        var s = CleanNumber(input);
        if (s == "") return false;
        var neg = false;
        switch (s[0])
        {
            case '-':
                neg = true;
                s = s[1..];
                break;
            case '+':
                s = s[1..];
                break;
        }
        if (s == "" || s[0] < '0' || s[0] > '9') return false;
        var lastSep = -1;
        var sameKind = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch >= '0' && ch <= '9') continue;
            if (ch != '.' && ch != ',') return false;
            if (lastSep >= 0 && s[lastSep] == ch) sameKind = true;
            lastSep = i;
        }
        var intPart = s;
        var fracPart = "";
        if (lastSep >= 0)
        {
            var after = s.Length - lastSep - 1;
            var decimalSep = !sameKind && !(s[lastSep] == '.' && after == 3 && s.Count(c => c == '.') == 1);
            if (decimalSep)
            {
                intPart = s[..lastSep];
                fracPart = s[(lastSep + 1)..];
            }
            if (!ValidGroups(intPart)) return false;
        }
        var digits = intPart.Replace(".", "").Replace(",", "") + fracPart;
        if (digits.Length > 18) return false;
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return false;
        value = neg ? -v : v;
        scale = fracPart.Length;
        return true;
    }

    static string CleanNumber(string s)
    {
        s = s.Trim().Replace("€", "").Replace("EUR", "").Replace("%", "").Replace(" ", "").Replace("–", "-");
        if (s.EndsWith('-')) s = s[..^1];
        if (s.EndsWith(',')) s += "00";
        return s.Trim();
    }

    static bool ValidGroups(string intPart)
    {
        var parts = intPart.Split(['.', ','], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length <= 1) return intPart.IndexOfAny(['.', ',']) < 0;
        if (parts[0].Length == 0 || parts[0].Length > 3) return false;
        return parts.Skip(1).All(p => p.Length == 3);
    }

    static long Rescale(long v, int from, int to)
    {
        for (; from < to; from++) v *= 10;
        if (from == to) return v;
        long div = 1;
        for (; from > to; from--) div *= 10;
        var half = div / 2;
        return v < 0 ? (v - half) / div : (v + half) / div;
    }

    public static long Number(string s, int scale)
    {
        foreach (var field in s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var tok = field.Split('/', 2)[0];
            if (TryParseNumber(tok, out var v, out var sc)) return Rescale(v, sc, scale);
        }
        return 0;
    }

    public static DateOnly? Date(string s) =>
        DateOnly.TryParseExact(s.Trim(), DateLayouts, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string UnitCode(string text)
    {
        text = text.Trim();
        if (Units.Resolve(text.Trim('.')) is { } code) return code;
        if (text.Contains(' ') || text.Contains('\t') || text.EnumerateRunes().Count() > MaxUnitLen) return "";
        return text;
    }
}

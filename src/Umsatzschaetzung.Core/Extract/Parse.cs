using System.Globalization;
using System.Text.RegularExpressions;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Extract;

public static class Parse
{
    const int MaxUnitLen = 8;

    public const int ScaleMilli = 3;
    public const int ScaleMicro = 6;
    public const int ScaleCents = 2;
    public const int ScaleBp = 2;

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

    // The grouped-thousands branch must require a separator group: alternation is leftmost-
    // first, so with `*` it matched the "78" of "78.49" and read 78,00. `;` and `:` are
    // separators because that is what the heavier scan profiles turn a comma into.
    static readonly Regex NumberRx =
        new(@"-?\d{1,3}(?:[.\s;:]\d{3})+(?:[.,;:]\d+)?|-?\d+(?:[.,;:]\d+)?");

    static readonly Regex Separator = new(@"[.,;:\s]");

    // German grouping is always three digits, so a final group of exactly two is the cents
    // whatever precedes it: `24.332.16` is 24 332,16, not 24 332.
    static bool IsDecimal(string[] groups, string matched)
    {
        if (groups.Length < 2) return false;
        var seps = Separator.Matches(matched);
        var last = seps[^1].Value[0];
        return last is ',' or ';' or ':'
            || groups[^1].Length == 2
            || (groups.Length == 2 && groups[^1].Length != 3);
    }

    public static long Number(string s, int scale)
    {
        var t = s.Trim().Replace('\u00a0', ' ');
        t = Currency.Replace(t, "").Trim();
        var neg = t.StartsWith('-') || t.EndsWith('-');
        t = t.Trim('-').Trim();
        var hit = NumberRx.Match(t);
        if (!hit.Success) return 0;

        var matched = hit.Value;
        var groups = Separator.Split(matched.TrimStart('-'));
        var text = IsDecimal(groups, matched)
            ? string.Concat(groups[..^1]) + "." + groups[^1]
            : string.Concat(groups);

        if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v))
            return 0;
        try
        {
            for (var i = 0; i < scale; i++) v *= 10m;
            return (long)Math.Round(neg ? -v : v, MidpointRounding.AwayFromZero);
        }
        catch (OverflowException)
        {
            return 0;
        }
    }

    static readonly Regex Currency = new(@"[€$%]|EUR|Eur");

    // Searches rather than matches: the date cell carries its label often enough
    // ("Rechnungsdatum: 08.11.2025") that anchoring loses it.
    static readonly (Regex Rx, int Y, int M, int D)[] DatePatterns =
    [
        (new(@"(?<!\d)(\d{4})-(\d{2})-(\d{2})(?!\d)"), 1, 2, 3),
        (new(@"(?<!\d)(\d{1,2})[.\-/](\d{1,2})[.\-/](\d{4})(?!\d)"), 3, 2, 1),
        (new(@"(?<!\d)(\d{1,2})[.\-/](\d{1,2})[.\-/](\d{2})(?!\d)"), 3, 2, 1),
    ];

    static readonly Regex NamedDate = new(@"(\d{1,2})\.?\s+(\p{L}+)\s+(\d{4})");

    static readonly string[] Months =
    [
        "januar", "februar", "märz", "april", "mai", "juni",
        "juli", "august", "september", "oktober", "november", "dezember",
    ];

    static readonly Dictionary<char, char> DateDigits = new()
    {
        ['O'] = '0', ['o'] = '0', ['I'] = '1', ['l'] = '1', ['|'] = '1',
    };

    static readonly Regex DateShape = new(@"^[\dOoIl|]{1,2}[.\-/][\dOoIl|]{1,2}[.\-/][\dOoIl|]{2,4}$");

    // Second attempt only, so a date that already reads is never touched. A month is repaired
    // only where exactly one of the twelve is one character away: `0ktober` comes back, `Ju1i`
    // — one from both juli and juni — does not.
    static string Unconfuse(string s)
    {
        var tokens = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < tokens.Length; i++)
        {
            var tok = tokens[i];
            if (DateShape.IsMatch(tok))
            {
                tokens[i] = string.Concat(tok.Select(c => DateDigits.GetValueOrDefault(c, c)));
                continue;
            }
            var bare = tok.Trim('.', ',', ';', ':').ToLowerInvariant();
            if (bare.Length <= 2) continue;
            var near = Months.Where(m => m.Length == bare.Length
                && m.Zip(bare).Count(p => p.First != p.Second) == 1).ToList();
            if (near.Count == 1) tokens[i] = near[0];
        }
        return string.Join(" ", tokens);
    }

    public static DateOnly? Date(string s) => Read(s.Trim()) ?? Read(Unconfuse(s.Trim()));

    static DateOnly? Read(string s)
    {
        foreach (var (rx, y, m, d) in DatePatterns)
        {
            var hit = rx.Match(s);
            if (!hit.Success) continue;
            var year = int.Parse(hit.Groups[y].Value, CultureInfo.InvariantCulture);
            return Make(year < 100 ? year + 2000 : year,
                int.Parse(hit.Groups[m].Value, CultureInfo.InvariantCulture),
                int.Parse(hit.Groups[d].Value, CultureInfo.InvariantCulture));
        }
        var named = NamedDate.Match(s);
        if (!named.Success) return null;
        var month = Array.IndexOf(Months, named.Groups[2].Value.ToLowerInvariant()) + 1;
        return month == 0 ? null
            : Make(int.Parse(named.Groups[3].Value, CultureInfo.InvariantCulture), month,
                int.Parse(named.Groups[1].Value, CultureInfo.InvariantCulture));
    }

    static DateOnly? Make(int year, int month, int day) =>
        month is >= 1 and <= 12 && day >= 1 && year is >= 1 and <= 9999 && day <= DateTime.DaysInMonth(year, month)
            ? new DateOnly(year, month, day)
            : null;

    // The litre in a pack size comes back as a one or a capital i: "Frittieröl 10 1" for
    // "Frittieröl 10 l". Only right after a number, where neither reads as a word, and only
    // inside an item name, where a lone digit has nothing to do anyway.
    public static string Litres(string name)
    {
        var parts = name.Split(' ');
        for (var i = 1; i < parts.Length; i++)
            if (parts[i] is "1" or "I" or "|" && parts[i - 1].Length > 0 && char.IsAsciiDigit(parts[i - 1][^1]))
                parts[i] = "l";
        return string.Join(' ', parts);
    }

    public static string UnitCode(string text)
    {
        text = text.Trim();
        // An e-invoice viewer can print the code in both the unit and the code column.
        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length > 1 && parts.All(p => p == parts[0])) text = parts[0];
        if (Units.Resolve(text.Trim('.')) is { } code) return code;
        if (Units.Unconfuse(text.Trim('.')) is { } repaired) return repaired;
        if (text.Contains(' ') || text.Contains('\t') || text.EnumerateRunes().Count() > MaxUnitLen) return "";
        return text;
    }
}

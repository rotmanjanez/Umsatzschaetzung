using System.Globalization;

namespace Umsatzschaetzung.App.Ui;

public static class Input
{
    public static long? Cents(string s) => Scaled(s, 2);
    public static long? Bp(string s) => Scaled(s, 2);
    public static long? Milli(string s) => Scaled(s, 3);
    public static long? Micro(string s) => Scaled(s, 6);

    public static long? Int(string s) =>
        long.TryParse(s.Replace(".", "").Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : null;

    public static DateOnly? Date(string s) =>
        DateOnly.TryParseExact(s.Trim(), "dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static string Edit(string display) => display.TrimEnd().TrimEnd('€', '%').TrimEnd();

    public static string BpText(long v)
    {
        var digits = Math.Abs(v).ToString(CultureInfo.InvariantCulture).PadLeft(3, '0');
        var text = (digits[..^2] + "," + digits[^2..]).TrimEnd('0').TrimEnd(',');
        return v < 0 ? "-" + text : text;
    }

    static long? Scaled(string s, int decimals)
    {
        s = Edit(s).Replace(".", "").Replace(" ", "");
        if (s == "") return null;
        var neg = s.StartsWith('-');
        if (neg) s = s[1..];
        var comma = s.IndexOf(',');
        var whole = comma < 0 ? s : s[..comma];
        var frac = comma < 0 ? "" : s[(comma + 1)..];
        if (frac.Length > decimals) frac = frac[..decimals];
        frac = frac.PadRight(decimals, '0');
        if (!long.TryParse(whole + frac, NumberStyles.None, CultureInfo.InvariantCulture, out var v)) return null;
        return neg ? -v : v;
    }
}

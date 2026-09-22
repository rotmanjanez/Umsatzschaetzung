using System.Text;

namespace Umsatzschaetzung.Model;

public static class Format
{
    public static string Cents(long v) => Fixed(v, 2, 2) + " €";

    public static string Bp(long v) => Fixed(v, 2, 0) + " %";

    public static string Milli(long v) => Fixed(v, 3, 0);

    public static string Micro(long v) => Fixed(v, 6, 2);

    public static string Date(DateOnly? d) => d is { } x ? $"{x.Day:D2}.{x.Month:D2}.{x.Year:D4}" : "";

    public static string EntityName(Entity e) => e switch
    {
        Entity.Category => "Kategorie",
        Entity.Ingredient => "Zutat",
        Entity.Mapping => "Zuordnung",
        Entity.Product => "Produkt",
        _ => "Ausbeuteregel",
    };

    public static string UnitName(Unit u) => u switch
    {
        Unit.Ml => "ml",
        Unit.G => "g",
        _ => "Stück",
    };

    public static string Qty(long n, Unit u) => u switch
    {
        Unit.Ml when Math.Abs(n) >= 1000 => Fixed(n, 3, 0) + " l",
        Unit.G when Math.Abs(n) >= 1000 => Fixed(n, 3, 0) + " kg",
        _ => Group(n) + " " + UnitName(u),
    };

    public static string Portions(long v) => Group(v) + (Math.Abs(v) == 1 ? " Portion" : " Portionen");

    public static string Sparte(Sparte s) => s switch
    {
        Model.Sparte.Getränke => "Getränke",
        Model.Sparte.Speisen => "Speisen",
        Model.Sparte.Handelsware => "Handelsware",
        _ => "Übrige",
    };

    public static string Source(Source s) => s switch
    {
        Model.Source.Ubl => "XRechnung (UBL)",
        Model.Source.Cii => "XRechnung (CII)",
        Model.Source.Zugferd => "ZUGFeRD",
        Model.Source.Scan => "Scan",
        _ => s.ToString(),
    };

    public static string Markup(long cost, long markup, long revenue) =>
        $"{Cents(cost)} × (100 % + {Bp(markup)}) {(cost + cost * markup / Model.Bp.Full == revenue ? "=" : "≈")} {Cents(revenue)}";

    public static string Period(DateOnly from, DateOnly to) => Date(from) + " bis " + Date(to);

    public static string Quantity(long qty, string unitCode) => (Milli(qty) + " " + Units.Label(unitCode)).Trim();

    public static string UnitPrice(long price, long baseQty, string unitCode) =>
        Micro(price) + " €" + (baseQty > 0 && baseQty != 1000 ? " je " + Milli(baseQty) + " " + Units.Label(unitCode) : "");

    public static string Verified(DateTimeOffset at, bool auto) =>
        (auto ? "automatisch geprüft am " : "geprüft am ") + Timestamp(at);

    public static string Timestamp(DateTimeOffset t) =>
        t == default ? "" : t.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

    public static string Day(DateTimeOffset t) =>
        t == default ? "" : t.ToLocalTime().ToString("dd.MM.yyyy");

    public static string Group(long n)
    {
        var s = Math.Abs(n).ToString();
        var b = new StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && (s.Length - i) % 3 == 0) b.Append('.');
            b.Append(s[i]);
        }
        return n < 0 ? "-" + b : b.ToString();
    }

    public static string Fixed(long n, int scale, int minDecimals)
    {
        long pow = 1;
        for (var i = 0; i < scale; i++) pow *= 10;
        var whole = Math.Abs(n) / pow;
        var frac = Math.Abs(n) % pow;
        var digits = frac.ToString().PadLeft(scale, '0');
        while (digits.Length > minDecimals && digits.EndsWith('0')) digits = digits[..^1];
        var output = Group(whole);
        if (n < 0 && (whole != 0 || frac != 0)) output = "-" + output;
        return digits == "" ? output : output + "," + digits;
    }
}

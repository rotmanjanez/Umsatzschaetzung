using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Umsatzschaetzung.Model;
using RxMatch = System.Text.RegularExpressions.Match;

namespace Umsatzschaetzung.Suggest;

// Size of one single container, expressed in the base unit it was written in.
// Base null means the number carried no unit and was read as litres or kilograms.
public sealed record Pack(long Count, long Size, Unit? Base);

public static partial class PackSize
{
    const long MaxCount = 2000;
    const long MaxSize = 200_000;

    [GeneratedRegex(@"(?:(?<c>\d{1,4})\s*(?:[x×*]|er)\s*)?(?<v>\d{1,5}(?:[.,]\d{1,3})?)\s*(?<u>[A-Za-zÄÖÜäöüß]{1,6})\.?(?![A-Za-zÄÖÜäöüß\d])", RegexOptions.IgnoreCase)]
    private static partial Regex Measured();

    [GeneratedRegex(@"(?<c>\d{1,4})\s*[x×*]\s*(?<v>\d{1,4}[.,]\d{1,3})(?![\d.,]*\s*[A-Za-zÄÖÜäöüß])")]
    private static partial Regex Bare();

    [GeneratedRegex(@"(?<c>\d{1,5})\s*(?:stk|stck|stück|stueck|st|pc)\.?(?![A-Za-zÄÖÜäöüß\d])", RegexOptions.IgnoreCase)]
    private static partial Regex Pieces();

    [GeneratedRegex(@"(?<c>\d{1,3})\s*er(?![A-Za-zÄÖÜäöüß\d])", RegexOptions.IgnoreCase)]
    private static partial Regex Multipack();

    [GeneratedRegex(@"[^\p{L}\p{N}]+")]
    private static partial Regex NotWord();

    [GeneratedRegex(@"(?<v>\d{1,5}[.,]\d{1,3})[1Il](?![\p{L}\p{N}])")]
    private static partial Regex GluedLitre();

    // "0,7 l" comes back from the recogniser as "0,71" -- the litre read as the digit it
    // is drawn like. Only where the line bills a container and the description yields no
    // pack size at all, so there is nothing to overwrite, and only if the split produces
    // one. Without the split the mapping needs a factor a human has to type in.
    public static string Recover(string text, string unitCode)
    {
        if (Units.Lookup(unitCode) is not { Container: true } || Read(text) is not null) return text;
        foreach (RxMatch m in GluedLitre().Matches(text))
        {
            var candidate = text[..m.Index] + m.Groups["v"].Value + " l" + text[(m.Index + m.Length)..];
            if (Read(candidate) is not null) return candidate;
        }
        return text;
    }

    // The description with the packaging taken out: the spans a pack size was read
    // from, plus the unit and container words. What is left is what the line is
    // about. Numbers outside a pack span stay — "Mehl Type 550" is not a size.
    public static string Strip(string text)
    {
        var b = new StringBuilder(text);
        foreach (RxMatch m in Measured().Matches(text))
            if (Units.Lookup(m.Groups["u"].Value) is { Base: not Unit.Piece }) Blank(b, m);
        foreach (RxMatch m in Bare().Matches(text)) Blank(b, m);
        foreach (RxMatch m in Pieces().Matches(text)) Blank(b, m);
        foreach (RxMatch m in Multipack().Matches(text)) Blank(b, m);
        return string.Join(' ', NotWord().Split(b.ToString()).Where(t => t != "" && Units.Lookup(t) is null));
    }

    static void Blank(StringBuilder b, RxMatch m)
    {
        for (var i = m.Index; i < m.Index + m.Length; i++) b[i] = ' ';
    }

    public static Pack? Read(string text)
    {
        foreach (RxMatch m in Measured().Matches(text))
        {
            if (Units.Lookup(m.Groups["u"].Value) is not { } u || u.Base == Unit.Piece) continue;
            if (Scaled(m.Groups["v"].Value, u.Factor) is not { } size) continue;
            return Pack(Count(m, text), size, u.Base);
        }
        if (Bare().Match(text) is { Success: true } b && Scaled(b.Groups["v"].Value, 1000) is { } bare)
            return Pack(Number(b.Groups["c"].Value), bare, null);
        if (Pieces().Match(text) is { Success: true } p)
            return Pack(Number(p.Groups["c"].Value), 1000, Unit.Piece);
        return null;
    }

    static Pack? Pack(long count, long size, Unit? unit) =>
        count is > 0 and <= MaxCount && size is > 0 and <= MaxSize ? new Pack(count, size, unit) : null;

    static long Count(RxMatch m, string text)
    {
        if (m.Groups["c"].Success) return Number(m.Groups["c"].Value);
        var pre = Multipack().Match(text[..m.Index]);
        if (pre.Success) return Number(pre.Groups["c"].Value);
        var pieces = Pieces().Match(text, m.Index + m.Length);
        return pieces.Success ? Number(pieces.Groups["c"].Value) : 1;
    }

    static long Number(string s) => long.Parse(s, CultureInfo.InvariantCulture);

    static long? Scaled(string s, long factor)
    {
        var size = (long)Math.Round(decimal.Parse(s.Replace(',', '.'), CultureInfo.InvariantCulture) * factor, MidpointRounding.AwayFromZero);
        return size > 0 ? size : null;
    }
}

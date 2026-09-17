using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Invoices;

public enum Kind { Ubl, Cii, Zugferd, Pdf, Image, Unknown }

public static class InvoiceParser
{
    internal const string RootUbl = "Invoice";
    internal const string RootCii = "CrossIndustryInvoice";

    public static Kind Detect(byte[] data)
    {
        if (IsPdf(data))
            return Pdf.TryExtractEmbeddedXml(data, out _) ? Kind.Zugferd : Kind.Pdf;
        if (IsPng(data) || IsJpeg(data) || IsTiff(data))
            return Kind.Image;
        return Xml.Root(data) switch
        {
            RootUbl => Kind.Ubl,
            RootCii => Kind.Cii,
            _ => Kind.Unknown,
        };
    }

    public static Model.Invoice Parse(string fileName, byte[] data)
    {
        var kind = Detect(data);
        Model.Invoice inv;
        try
        {
            inv = kind switch
            {
                Kind.Ubl => Ubl.Parse(data),
                Kind.Cii => Cii.Parse(data),
                Kind.Zugferd => Cii.Parse(Pdf.ExtractEmbeddedXml(data)),
                _ => throw new InvalidDataException("unknown invoice format"),
            };
        }
        catch (Exception e) when (e is InvalidDataException or XmlException)
        {
            throw new InvalidDataException($"{fileName}: {e.Message}", e);
        }
        inv.Source = kind switch
        {
            Kind.Ubl => Source.Ubl,
            Kind.Cii => Source.Cii,
            _ => Source.Zugferd,
        };
        inv.FileName = fileName;
        return inv;
    }

    static bool IsPdf(byte[] data) =>
        data.AsSpan(0, Math.Min(data.Length, 1024)).IndexOf("%PDF-"u8) >= 0;

    static bool IsPng(byte[] data) => data.AsSpan().StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

    static bool IsJpeg(byte[] data) => data.AsSpan().StartsWith(new byte[] { 0xFF, 0xD8, 0xFF });

    static bool IsTiff(byte[] data) =>
        data.AsSpan().StartsWith("II*\0"u8) || data.AsSpan().StartsWith("MM\0*"u8);
}

internal static class Xml
{
    static readonly XmlReaderSettings Settings = new()
    {
        DtdProcessing = DtdProcessing.Ignore,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreProcessingInstructions = true,
    };

    public static string Root(byte[] data)
    {
        if (data.AsSpan(0, Math.Min(data.Length, 4096)).IndexOf((byte)'<') < 0)
            return "";
        try
        {
            using var reader = XmlReader.Create(new StringReader(Decode(data)), Settings);
            while (reader.Read())
                if (reader.NodeType == XmlNodeType.Element)
                    return reader.LocalName;
        }
        catch (Exception e) when (e is XmlException or InvalidDataException)
        {
        }
        return "";
    }

    public static XElement Load(byte[] data)
    {
        using var reader = XmlReader.Create(new StringReader(Decode(data)), Settings);
        return XDocument.Load(reader).Root ?? throw new InvalidDataException("empty document");
    }

    static string Decode(byte[] data)
    {
        var span = data.AsSpan();
        if (span.StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }))
            span = span[3..];
        var charset = DeclaredCharset(span);
        var text = charset switch
        {
            "" or "utf-8" or "utf8" or "us-ascii" or "ascii" => Encoding.UTF8.GetString(span),
            "iso-8859-1" or "iso8859-1" or "latin1" or "windows-1252" or "cp1252" => Encoding.Latin1.GetString(span),
            _ => throw new InvalidDataException($"unsupported charset \"{charset}\""),
        };
        return text.TrimStart();
    }

    static string DeclaredCharset(ReadOnlySpan<byte> data)
    {
        var head = Encoding.Latin1.GetString(data[..Math.Min(data.Length, 256)]);
        if (!head.StartsWith("<?xml"))
            return "";
        var end = head.IndexOf("?>", StringComparison.Ordinal);
        if (end < 0)
            return "";
        var decl = head[..end];
        var i = decl.IndexOf("encoding", StringComparison.Ordinal);
        if (i < 0)
            return "";
        var q = decl.IndexOfAny(['"', '\''], i);
        if (q < 0)
            return "";
        var close = decl.IndexOf(decl[q], q + 1);
        return close < 0 ? "" : decl[(q + 1)..close].ToLowerInvariant();
    }

    public static IEnumerable<XElement> Path(XElement root, string path)
    {
        IEnumerable<XElement> current = [root];
        foreach (var name in path.Split('>'))
            current = current.SelectMany(e => e.Elements().Where(c => c.Name.LocalName == name));
        return current;
    }

    public static XElement? Last(XElement root, string path) => Path(root, path).LastOrDefault();

    public static string Text(XElement? e)
    {
        if (e is null)
            return "";
        var sb = new StringBuilder();
        foreach (var node in e.Nodes())
            if (node is XText t)
                sb.Append(t.Value);
        return sb.ToString();
    }

    public static string Text(XElement root, string path) => Text(Last(root, path));

    public static string Attr(XElement? e, string name) =>
        e?.Attributes().FirstOrDefault(a => a.Name.LocalName == name)?.Value ?? "";

    public static List<string> Texts(XElement root, string path) => Path(root, path).Select(Text).ToList();
}

internal static class Numbers
{
    public static long ParseScaled(string s, int scale, bool round)
    {
        s = s.Trim();
        if (s.Length == 0)
            throw new InvalidDataException("empty number");
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
        var dot = s.IndexOf('.');
        var intPart = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? "" : s[(dot + 1)..];
        if (intPart.Length == 0 && frac.Length == 0 || !Digits(intPart) || !Digits(frac))
            throw new InvalidDataException($"invalid number \"{s}\"");
        var roundUp = false;
        if (frac.Length > scale)
        {
            var extra = frac[scale..];
            if (!round && extra.Trim('0').Length > 0)
                throw new InvalidDataException($"number \"{s}\" has more than {scale} decimals");
            roundUp = extra[0] >= '5';
            frac = frac[..scale];
        }
        frac += new string('0', scale - frac.Length);
        if (!long.TryParse("0" + intPart + frac, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            throw new InvalidDataException($"number \"{s}\": value out of range");
        if (roundUp)
            v++;
        return neg ? -v : v;
    }

    static bool Digits(string s)
    {
        foreach (var c in s)
            if (c < '0' || c > '9')
                return false;
        return true;
    }

    public static long Cents(string s) => ParseScaled(s, 2, false);
    public static long Milli(string s) => ParseScaled(s, 3, false);
    public static long Micro(string s) => ParseScaled(s, 6, true);
    public static long Bp(string percent) => ParseScaled(percent, 2, false);
    public static long OptionalCents(string s) => s.Trim().Length == 0 ? 0 : Cents(s);
    public static long OptionalBp(string s) => s.Trim().Length == 0 ? 0 : Bp(s);
    public static long BaseQty(string s) => s.Trim().Length == 0 ? 1000 : Milli(s);

    // The invoice date is optional: an absent or unreadable one leaves the field empty
    // instead of rejecting the document.
    public static DateOnly? OptionalDate(string s, params string[] formats)
    {
        s = s.Trim();
        foreach (var f in formats)
            if (DateOnly.TryParseExact(s, f, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d;
        return null;
    }

    public static long LineNo(string id, int index) =>
        long.TryParse(id.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : index + 1;

    public static string? Optional(string s) => s.Trim() is { Length: > 0 } t ? t : null;

    public static T Wrap<T>(string context, Func<T> f)
    {
        try
        {
            return f();
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{context}: {e.Message}", e);
        }
    }
}

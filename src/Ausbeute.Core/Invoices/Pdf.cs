using System.IO.Compression;

namespace Ausbeute.Invoices;

public static class Pdf
{
    public static byte[] ExtractEmbeddedXml(byte[] pdf) =>
        TryExtractEmbeddedXml(pdf, out var xml) ? xml : throw new InvalidDataException("pdf: no embedded CrossIndustryInvoice");

    public static bool TryExtractEmbeddedXml(byte[] pdf, out byte[] xml)
    {
        var span = pdf.AsSpan();
        var pos = 0;
        int i;
        while ((i = span[pos..].IndexOf("stream"u8)) >= 0)
        {
            var kw = pos + i;
            pos = kw + 6;
            if (kw >= 3 && span.Slice(kw - 3, 3).SequenceEqual("end"u8)) continue;
            var obj = span[..kw].LastIndexOf("obj"u8);
            if (obj < 0) continue;
            var dict = span[obj..kw];
            if (dict.IndexOf("/Image"u8) >= 0 || dict.IndexOf("/Font"u8) >= 0) continue;
            var start = pos;
            if (start < span.Length && span[start] == '\r') start++;
            if (start < span.Length && span[start] == '\n') start++;
            var end = span[start..].IndexOf("endstream"u8);
            if (end < 0) break;
            var data = span.Slice(start, end).TrimEnd("\r\n"u8);
            byte[] content;
            try
            {
                content = dict.IndexOf("/FlateDecode"u8) >= 0 ? Inflate(data) : data.ToArray();
            }
            catch (Exception e) when (e is InvalidDataException or IOException)
            {
                continue;
            }
            if (Xml.Root(content) == InvoiceParser.RootCii)
            {
                xml = content;
                return true;
            }
        }
        xml = [];
        return false;
    }

    static byte[] Inflate(ReadOnlySpan<byte> data)
    {
        using var input = new MemoryStream(data.ToArray());
        using var z = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        z.CopyTo(output);
        return output.ToArray();
    }
}

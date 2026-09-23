using System.Security;
using System.Text.RegularExpressions;
using Avalonia.Media.Imaging;
using SkiaSharp;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public static partial class Snippet
{
    const int Pad = 12;

    // The row of the item table the line was read from, across the width of the table. Encoded on its
    // own, so the decoded page is not held while the row is shown.
    public static Bitmap? Crop(List<OcrPage> pages, int index, InvoiceLine line)
    {
        var read = pages.SelectMany(p => p.Lines.Select(l => (Page: p, Line: l))).ToList();
        var at = Pick(read.Count, index, i => read[i].Line.Parsed.Name == line.Name);
        if (at < 0 || read[at].Page is not { Image.Length: > 0 } page || read[at].Line.Cells.Count == 0) return null;
        var table = page.Lines.SelectMany(l => l.Cells.Values).Select(c => c.Box).ToList();
        var row = read[at].Line.Cells.Values.Select(c => c.Box).ToList();
        using var image = SKBitmap.Decode(page.Image);
        if (image is null) return null;
        var scale = page.Width > 0 ? (double)image.Width / page.Width : 1;
        var rect = SKRectI.Intersect(new SKRectI(
                (int)((table.Min(b => b.X) - Pad) * scale),
                (int)((row.Min(b => b.Y) - Pad) * scale),
                (int)((table.Max(b => b.X + b.W) + Pad) * scale),
                (int)((row.Max(b => b.Y + b.H) + Pad) * scale)),
            new SKRectI(0, 0, image.Width, image.Height));
        if (rect.Width <= 0 || rect.Height <= 0) return null;
        using var cut = new SKBitmap();
        if (!image.ExtractSubset(cut, rect)) return null;
        using var cropped = SKImage.FromBitmap(cut);
        using var png = cropped.Encode(SKEncodedImageFormat.Png, 100);
        return new Bitmap(png.AsStream());
    }

    // The line's element of an e-invoice, as it stands in the file.
    public static string? Excerpt(string xml, int index, InvoiceLine line)
    {
        var items = LineElement().Matches(xml);
        var escaped = SecurityElement.Escape(line.Name);
        var at = Pick(items.Count, index, i => items[i].Value.Contains(line.Name) || items[i].Value.Contains(escaped));
        if (at < 0) return null;
        var m = items[at];
        var indent = m.Index - xml.LastIndexOf('\n', m.Index) - 1;
        return string.Join('\n', m.Value.Split('\n').Select((l, i) =>
            (i == 0 ? l : l[Math.Min(indent, l.Length - l.TrimStart().Length)..]).TrimEnd('\r')));
    }

    // The same position first, as the lines were stored in document order; a name elsewhere when
    // lines were added or removed since.
    static int Pick(int count, int index, Func<int, bool> matches)
    {
        if (index < count && matches(index)) return index;
        for (var i = 0; i < count; i++)
            if (matches(i)) return i;
        return index < count ? index : -1;
    }

    [GeneratedRegex(@"<(?:[\w.-]+:)?(InvoiceLine|CreditNoteLine|IncludedSupplyChainTradeLineItem)[\s>][\s\S]*?</(?:[\w.-]+:)?\1>")]
    private static partial Regex LineElement();
}

using SkiaSharp;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

public static class Rows
{
    const int Pad = 12;

    // The row of the item table the line was read from, across the width of the table, in the
    // frame of the reading.
    public static (int Page, SKRectI Box)? Of(List<OcrPage> pages, int index, string name)
    {
        var read = pages.SelectMany((p, i) => p.Lines.Select(l => (Page: i, Line: l))).ToList();
        var at = Pick(read.Count, index, i => read[i].Line.Parsed.Name == name);
        if (at < 0 || read[at].Line.Cells.Count == 0) return null;
        var (page, line) = read[at];
        var table = pages[page].Lines.SelectMany(l => l.Cells.Values).Select(c => c.Box).ToList();
        var row = line.Cells.Values.Select(c => c.Box).ToList();
        return (page, new SKRectI(
            table.Min(b => b.X) - Pad, row.Min(b => b.Y) - Pad,
            table.Max(b => b.X + b.W) + Pad, row.Max(b => b.Y + b.H) + Pad));
    }

    // The same position first, as the lines were stored in document order; a name elsewhere when
    // lines were added or removed since.
    public static int Pick(int count, int index, Func<int, bool> matches)
    {
        if (index < count && matches(index)) return index;
        for (var i = 0; i < count; i++)
            if (matches(i)) return i;
        return index < count ? index : -1;
    }
}

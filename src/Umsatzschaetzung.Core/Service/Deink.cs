using System.Runtime.InteropServices;
using SkiaSharp;

namespace Umsatzschaetzung.Service;

// Removes show-through: the reverse of a double-sided sheet, which lands in the same rows
// as the front and invents line items. A stroke is a connected run of marks, kept if any
// part of it reaches half the ink level. Paper passes far less than half of what is printed
// on its other side, while grey or coloured print on the front does reach it: measured on
// the fixtures, a rendered PDF has no stroke under 0.55 of its ink level and double-sided
// scans pile up under 0.5.
public static class Deink
{
    const int Limit = RapidOcr.MaxImageDimension;

    public static SKBitmap? Apply(SKBitmap page)
    {
        var scale = Math.Min(1.0, (double)Limit / Math.Max(page.Width, page.Height));
        int w = Math.Max((int)(page.Width * scale), 1), h = Math.Max((int)(page.Height * scale), 1);
        var grey = Ink.Grey(page, w, h);
        var depth = Ink.Depth(grey, w, h);
        var histogram = Ink.Histogram(depth);
        var marked = Ink.Otsu(histogram);
        var floor = (byte)(Ink.Median(histogram, Ink.Otsu(histogram, marked)) / 2);

        var strokes = new Strokes(depth, w, h, marked, floor);
        if (!strokes.Any) return null;

        var clean = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        Parallel.For(0, h, y =>
        {
            var row = clean.GetPixelSpan().Slice(y * w * 4, w * 4);
            var pixels = MemoryMarshal.Cast<byte, uint>(row);
            pixels.Fill(0xFFFFFFFFu);
            foreach (var (from, to) in strokes.Kept(y))
                for (var x = from; x < to; x++)
                    pixels[x] = 0xFF000000u | grey[y * w + x] * 0x010101u;
        });
        clean.NotifyPixelsChanged();
        return clean;
    }

    // The marks of a row as runs, joined to the runs of the row above that touch them, even
    // at a corner. A stroke is a set of joined runs and reaches if any of them does.
    sealed class Strokes
    {
        readonly int[] first;
        readonly int[] from, to, parent;
        readonly bool[] reaches;

        public bool Any { get; }

        public Strokes(byte[] depth, int w, int h, byte marked, byte floor)
        {
            var rows = new (int From, int To, bool Reaches)[h][];
            Parallel.For(0, h, y => rows[y] = Runs(depth.AsSpan(y * w, w), marked, floor));

            first = new int[h + 1];
            for (var y = 0; y < h; y++) first[y + 1] = first[y] + rows[y].Length;
            var count = first[h];
            from = new int[count];
            to = new int[count];
            parent = new int[count];
            reaches = new bool[count];
            for (var y = 0; y < h; y++)
                for (var i = 0; i < rows[y].Length; i++)
                {
                    var at = first[y] + i;
                    (from[at], to[at], reaches[at]) = rows[y][i];
                    parent[at] = at;
                }

            for (var y = 1; y < h; y++)
            {
                int above = first[y - 1], end = first[y];
                for (var at = first[y]; at < first[y + 1]; at++)
                {
                    while (above < end && to[above] < from[at]) above++;
                    for (var up = above; up < end && from[up] <= to[at]; up++) Join(at, up);
                }
            }

            var any = false;
            for (var at = 0; at < count; at++)
                if (reaches[at]) reaches[Root(at)] = any = true;
            for (var at = 0; at < count; at++) reaches[at] = reaches[Root(at)];
            Any = any;
        }

        public IEnumerable<(int From, int To)> Kept(int y)
        {
            for (var at = first[y]; at < first[y + 1]; at++)
                if (reaches[at]) yield return (from[at], to[at]);
        }

        static (int, int, bool)[] Runs(ReadOnlySpan<byte> row, byte marked, byte floor)
        {
            var runs = new List<(int, int, bool)>();
            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] <= marked) continue;
                var start = x;
                var reaches = false;
                for (; x < row.Length && row[x] > marked; x++)
                    if (row[x] >= floor) reaches = true;
                runs.Add((start, x, reaches));
            }
            return [.. runs];
        }

        void Join(int a, int b)
        {
            a = Root(a);
            b = Root(b);
            if (a != b) parent[Math.Max(a, b)] = Math.Min(a, b);
        }

        int Root(int at)
        {
            while (parent[at] != at) at = parent[at] = parent[parent[at]];
            return at;
        }
    }
}

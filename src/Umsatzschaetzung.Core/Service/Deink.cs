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
        var marked = Ink.Otsu(depth);
        var floor = (byte)(Ink.Median(depth, Ink.Otsu(depth, marked)) / 2);

        var keep = Strokes(depth, w, h, marked, floor);
        if (!keep.Contains(true)) return null;

        var buffer = new byte[w * h * 4];
        for (var i = 0; i < keep.Length; i++)
        {
            var v = keep[i] ? grey[i] : (byte)255;
            buffer[i * 4] = v;
            buffer[i * 4 + 1] = v;
            buffer[i * 4 + 2] = v;
            buffer[i * 4 + 3] = 255;
        }
        var clean = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul));
        Marshal.Copy(buffer, 0, clean.GetPixels(), buffer.Length);
        return clean;
    }

    static bool[] Strokes(byte[] depth, int w, int h, byte marked, byte floor)
    {
        var keep = new bool[depth.Length];
        var seen = new bool[depth.Length];
        var stack = new Stack<int>();
        var stroke = new List<int>();
        for (var start = 0; start < depth.Length; start++)
        {
            if (seen[start] || depth[start] <= marked) continue;
            stroke.Clear();
            stack.Push(start);
            seen[start] = true;
            var reaches = false;
            while (stack.Count > 0)
            {
                var at = stack.Pop();
                stroke.Add(at);
                if (depth[at] >= floor) reaches = true;
                int x = at % w, y = at / w;
                for (var dy = -1; dy <= 1; dy++)
                    for (var dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                        var next = ny * w + nx;
                        if (seen[next] || depth[next] <= marked) continue;
                        seen[next] = true;
                        stack.Push(next);
                    }
            }
            if (!reaches) continue;
            foreach (var at in stroke) keep[at] = true;
        }
        return keep;
    }
}

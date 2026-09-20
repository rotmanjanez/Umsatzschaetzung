using System.Runtime.InteropServices;
using SkiaSharp;

namespace Umsatzschaetzung.Service;

// Suppresses show-through: the reverse of a double-sided sheet, picked up through the
// paper. It reads as text — mirrored, or upside down where the sheet was fed the other
// way — and lands in the same rows as the front, so it invents line items, buries unit
// codes and corrupts totals. Nothing here is tuned; every level comes from the page.
public static class Deink
{
    // Beyond this the detector downscales anyway, so cleaning finer detail is wasted and
    // the buffers would be enormous.
    const int Limit = RapidOcr.MaxImageDimension;

    public static SKBitmap? Apply(SKBitmap page)
    {
        var scale = Math.Min(1.0, (double)Limit / Math.Max(page.Width, page.Height));
        int w = Math.Max((int)(page.Width * scale), 1), h = Math.Max((int)(page.Height * scale), 1);
        var grey = Ink.Grey(page, w, h);
        var depth = Ink.Depth(grey, w, h);

        var marked = Ink.Otsu(depth);
        // The marked pixels split again by their own histogram: what the reverse of the
        // sheet leaves is shallower than what the front prints. Where they do not split
        // — a single-sided page, or one whose show-through the first level already put
        // back with the paper — every mark is front ink and stands.
        var core = Ink.Otsu(depth, marked);
        if (core <= marked) core = (byte)Math.Min(marked + 1, 255);

        var keep = Strokes(depth, w, h, marked, core);
        var kept = 0;
        foreach (var k in keep) if (k) kept++;
        if (kept == 0) return null;

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

    // A stroke is kept whole if any part of it reaches the front-ink level: faint edge
    // pixels belong to the glyph they touch, and cutting per pixel eats thin strokes —
    // measured, it turned Schweineschnitzel into Schyeineschnitzel. Show-through has no
    // core to be reached from and falls out entire.
    static bool[] Strokes(byte[] depth, int w, int h, byte marked, byte core)
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
                if (depth[at] >= core) reaches = true;
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

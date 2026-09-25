using SkiaSharp;

namespace Umsatzschaetzung.Service;

// Ink depth: how far below the local paper level a pixel sits, so uneven lighting, a grey
// sheet and a vignette cancel.
static class Ink
{
    const int Cells = 16;

    public static byte[] Grey(SKBitmap page, int w, int h)
    {
        var same = page.Width == w && page.Height == h;
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var small = same && page.ColorType == SKColorType.Bgra8888 && page.RowBytes == w * 4 ? null
            : (same ? page.Copy(SKColorType.Bgra8888) : page.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)))
              ?? throw new InvalidOperationException("Das Seitenbild konnte nicht umgewandelt werden.");
        var pixels = (small ?? page).GetPixelSpan();
        var grey = new byte[w * h];
        for (var i = 0; i < grey.Length; i++)
            grey[i] = (byte)((pixels[i * 4 + 2] * 77 + pixels[i * 4 + 1] * 151 + pixels[i * 4] * 28) >> 8);
        return grey;
    }

    // Paper is the brightest value per cell, interpolated between cells.
    public static byte[] Depth(byte[] grey, int w, int h)
    {
        int cw = Math.Max(w / Cells, 1), ch = Math.Max(h / Cells, 1);
        int gx = (w + cw - 1) / cw, gy = (h + ch - 1) / ch;
        var cell = new byte[gx * gy];
        for (var cy = 0; cy < gy; cy++)
            for (var cx = 0; cx < gx; cx++)
            {
                byte brightest = 0;
                for (var y = cy * ch; y < Math.Min((cy + 1) * ch, h); y++)
                    for (var x = cx * cw; x < Math.Min((cx + 1) * cw, w); x++)
                        if (grey[y * w + x] > brightest) brightest = grey[y * w + x];
                cell[cy * gx + cx] = brightest;
            }

        var depth = new byte[w * h];
        for (var y = 0; y < h; y++)
        {
            var fy = Math.Clamp((y + 0.5) / ch - 0.5, 0, gy - 1.0);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, gy - 1);
            var ty = fy - y0;
            for (var x = 0; x < w; x++)
            {
                var fx = Math.Clamp((x + 0.5) / cw - 0.5, 0, gx - 1.0);
                int x0 = (int)fx, x1 = Math.Min(x0 + 1, gx - 1);
                var tx = fx - x0;
                var top = cell[y0 * gx + x0] * (1 - tx) + cell[y0 * gx + x1] * tx;
                var bottom = cell[y1 * gx + x0] * (1 - tx) + cell[y1 * gx + x1] * tx;
                depth[y * w + x] = (byte)Math.Clamp(top * (1 - ty) + bottom * ty - grey[y * w + x], 0, 255);
            }
        }
        return depth;
    }

    public static byte Median(byte[] values, int above)
    {
        Span<long> hist = stackalloc long[256];
        long count = 0;
        foreach (var v in values)
            if (v > above) { hist[v]++; count++; }
        if (count == 0) return 255;
        long seen = 0;
        for (var i = 0; i < 256; i++)
        {
            seen += hist[i];
            if (seen * 2 >= count) return (byte)i;
        }
        return 255;
    }

    // Between-class variance maximiser over the values above the given level.
    public static byte Otsu(byte[] values, int above = -1)
    {
        Span<long> hist = stackalloc long[256];
        long count = 0;
        foreach (var v in values)
            if (v > above) { hist[v]++; count++; }
        if (count == 0) return 0;

        double all = 0;
        for (var i = 0; i < 256; i++) all += i * (double)hist[i];
        double below = 0, sum = 0, top = -1;
        var cut = 0;
        for (var i = 0; i < 256; i++)
        {
            below += hist[i];
            var rest = count - below;
            if (below == 0 || rest <= 0) continue;
            sum += i * (double)hist[i];
            var gap = sum / below - (all - sum) / rest;
            var between = below * rest * gap * gap;
            if (between <= top) continue;
            top = between;
            cut = i;
        }
        return (byte)cut;
    }
}

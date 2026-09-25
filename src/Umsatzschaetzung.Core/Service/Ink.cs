using RapidOcrNet;
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
            : (same ? page.Copy(SKColorType.Bgra8888) : Bands.Resize(page, info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None)))
              ?? throw new InvalidOperationException("Das Seitenbild konnte nicht umgewandelt werden.");
        var source = small ?? page;
        var grey = new byte[w * h];
        Parallel.For(0, h, y =>
        {
            var pixels = source.GetPixelSpan().Slice(y * w * 4, w * 4);
            var row = grey.AsSpan(y * w, w);
            for (var x = 0; x < row.Length; x++)
                row[x] = (byte)((pixels[x * 4 + 2] * 77 + pixels[x * 4 + 1] * 151 + pixels[x * 4] * 28) >> 8);
        });
        return grey;
    }

    // Paper is the brightest value per cell, interpolated between cells.
    public static byte[] Depth(byte[] grey, int w, int h)
    {
        int cw = Math.Max(w / Cells, 1), ch = Math.Max(h / Cells, 1);
        int gx = (w + cw - 1) / cw, gy = (h + ch - 1) / ch;
        var cell = new byte[gx * gy];
        Parallel.For(0, gy, cy =>
        {
            for (var cx = 0; cx < gx; cx++)
            {
                byte brightest = 0;
                for (var y = cy * ch; y < Math.Min((cy + 1) * ch, h); y++)
                    for (var x = cx * cw; x < Math.Min((cx + 1) * cw, w); x++)
                        if (grey[y * w + x] > brightest) brightest = grey[y * w + x];
                cell[cy * gx + cx] = brightest;
            }
        });

        var x0 = new int[w];
        var x1 = new int[w];
        var tx = new double[w];
        for (var x = 0; x < w; x++)
        {
            var fx = Math.Clamp((x + 0.5) / cw - 0.5, 0, gx - 1.0);
            x0[x] = (int)fx;
            x1[x] = Math.Min(x0[x] + 1, gx - 1);
            tx[x] = fx - x0[x];
        }

        var depth = new byte[w * h];
        Parallel.For(0, h, y =>
        {
            var fy = Math.Clamp((y + 0.5) / ch - 0.5, 0, gy - 1.0);
            int y0 = (int)fy, y1 = Math.Min(y0 + 1, gy - 1);
            var ty = fy - y0;
            var above = cell.AsSpan(y0 * gx, gx);
            var below = cell.AsSpan(y1 * gx, gx);
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var top = above[x0[x]] * (1 - tx[x]) + above[x1[x]] * tx[x];
                var bottom = below[x0[x]] * (1 - tx[x]) + below[x1[x]] * tx[x];
                depth[row + x] = (byte)Math.Clamp(top * (1 - ty) + bottom * ty - grey[row + x], 0, 255);
            }
        });
        return depth;
    }

    public static long[] Histogram(byte[] values)
    {
        const int chunk = 1 << 18;
        var hist = new long[256];
        Parallel.For(0, (values.Length + chunk - 1) / chunk, () => new long[256], (c, _, part) =>
        {
            foreach (var v in values.AsSpan(c * chunk, Math.Min(chunk, values.Length - c * chunk))) part[v]++;
            return part;
        }, part =>
        {
            lock (hist)
                for (var i = 0; i < hist.Length; i++) hist[i] += part[i];
        });
        return hist;
    }

    public static byte Median(long[] histogram, int above)
    {
        var hist = histogram.AsSpan(above + 1);
        long count = 0;
        foreach (var n in hist) count += n;
        if (count == 0) return 255;
        long seen = 0;
        for (var i = 0; i < hist.Length; i++)
        {
            seen += hist[i];
            if (seen * 2 >= count) return (byte)(above + 1 + i);
        }
        return 255;
    }

    // Between-class variance maximiser over the values above the given level.
    public static byte Otsu(long[] histogram, int above = -1)
    {
        Span<long> hist = stackalloc long[256];
        histogram.AsSpan(above + 1).CopyTo(hist[(above + 1)..]);
        long count = 0;
        foreach (var n in hist) count += n;
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

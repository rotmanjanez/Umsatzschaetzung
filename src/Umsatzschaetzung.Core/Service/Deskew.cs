using SkiaSharp;

namespace Umsatzschaetzung.Service;

// Straightens a page before it is read. Word grouping downstream bands by y, so once the
// lean carries a line further than half a row height across the sheet, the far end of one
// line joins the next: names lose their head, phantom lines appear, whole lines go missing.
public static class Deskew
{
    // A sheet feeder misfeeds by degrees, not minutes — the worst real scan measured 4.9.
    const double Limit = 6.0;
    const int Steps = 21;
    const int Rounds = 3;

    // The estimate needs shape, not detail, and the sweep costs a pass per candidate angle.
    const int Working = 1200;

    // The page border — edge shadow, vignette, the sheet boundary — stays axis aligned
    // however the sheet lay. Left in, it outvotes the text it frames and the page is
    // declared straight: measured against the generated corpus, cropping it lifts the
    // pages that lock on from 28/40 to 39/40. How much is cropped barely matters.
    const double Margin = 0.15;

    public static SKBitmap? Apply(SKBitmap page) => Apply(page, out _);

    public static SKBitmap? Apply(SKBitmap page, out double applied)
    {
        var angle = Angle(page);
        applied = Moves(angle, page.Width) ? angle : 0;
        return applied == 0 ? null : Straighten(page, angle);
    }

    // Below a pixel of travel across the page there is nothing to correct and a resample
    // would only cost the thin strokes.
    static bool Moves(double degrees, int width) => Math.Abs(Math.Tan(Radians(degrees))) * width >= 1.0;

    public static double Angle(SKBitmap page)
    {
        var (ink, w, h) = Mask(page);
        return w < 8 || h < 8 ? 0 : Sharpest(ink, w, h);
    }

    static double Sharpest(byte[] ink, int w, int h)
    {
        double lo = -Limit, hi = Limit, best = 0, top = double.MinValue;
        for (var round = 0; round < Rounds; round++)
        {
            var step = (hi - lo) / (Steps - 1);
            top = double.MinValue;
            for (var i = 0; i < Steps; i++)
            {
                var angle = lo + step * i;
                var score = Peakedness(ink, w, h, angle);
                // A page without bands scores the same at every angle and stays as it is.
                if (score < top || score == top && Math.Abs(angle) >= Math.Abs(best)) continue;
                top = score;
                best = angle;
            }
            lo = Math.Max(best - step, -Limit);
            hi = Math.Min(best + step, Limit);
        }
        return best;
    }

    // Straight text piles into sharp bands and the row profile spikes; a lean smears it.
    static double Peakedness(byte[] ink, int w, int h, double degrees)
    {
        var slope = Math.Tan(Radians(degrees));
        var whole = new int[w];
        var part = new float[w];
        int low = 0, high = 0;
        for (var x = 0; x < w; x++)
        {
            var offset = slope * x;
            whole[x] = (int)Math.Floor(offset);
            part[x] = (float)(offset - whole[x]);
            if (whole[x] < low) low = whole[x];
            if (whole[x] > high) high = whole[x];
        }
        var profile = new float[h + (high - low) + 2];
        for (var y = 0; y < h; y++)
        {
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                if (ink[row + x] == 0) continue;
                var at = y + whole[x] - low;
                profile[at] += 1 - part[x];
                profile[at + 1] += part[x];
            }
        }
        double mean = 0;
        foreach (var v in profile) mean += v;
        mean /= profile.Length;
        double spread = 0;
        foreach (var v in profile) spread += (v - mean) * (v - mean);
        return spread / profile.Length;
    }

    static (byte[] Mask, int W, int H) Mask(SKBitmap page)
    {
        var scale = Math.Min(1.0, (double)Working / Math.Max(page.Width, page.Height));
        var w = Math.Max((int)(page.Width * scale), 1);
        var h = Math.Max((int)(page.Height * scale), 1);
        var depth = Ink.Depth(Ink.Grey(page, w, h), w, h);
        var cut = Ink.Otsu(depth);

        int x0 = (int)(w * Margin), y0 = (int)(h * Margin);
        int cw = Math.Max(w - 2 * x0, 1), ch = Math.Max(h - 2 * y0, 1);
        var ink = new byte[cw * ch];
        for (var y = 0; y < ch; y++)
            for (var x = 0; x < cw; x++)
                ink[y * cw + x] = depth[(y + y0) * w + x + x0] > cut ? (byte)1 : (byte)0;
        return (ink, cw, ch);
    }

    public static SKBitmap Straighten(SKBitmap page, double degrees)
    {
        var (map, size) = Straightening(new SKSizeI(page.Width, page.Height), degrees);
        var turned = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(turned);
        canvas.Clear(SKColors.White);
        canvas.Concat(map);
        using var image = SKImage.FromBitmap(page);
        canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        return turned;
    }

    // Turned about its centre onto a canvas that holds all of it.
    public static (SKMatrix Map, SKSizeI Size) Straightening(SKSizeI page, double degrees)
    {
        var radians = Math.Abs(Radians(degrees));
        double cos = Math.Cos(radians), sin = Math.Sin(radians);
        var width = (int)Math.Ceiling(page.Width * cos + page.Height * sin);
        var height = (int)Math.Ceiling(page.Width * sin + page.Height * cos);
        var map = SKMatrix.CreateTranslation(width / 2f, height / 2f)
            .PreConcat(SKMatrix.CreateRotationDegrees((float)degrees))
            .PreConcat(SKMatrix.CreateTranslation(-page.Width / 2f, -page.Height / 2f));
        return (map, new SKSizeI(width, height));
    }

    static double Radians(double degrees) => degrees * Math.PI / 180;
}

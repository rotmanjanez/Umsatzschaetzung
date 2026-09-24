using SkiaSharp;

namespace Umsatzschaetzung.Tests.Scan;

static class Sheets
{
    public static SKBitmap Blank(int width = 600, int height = 800, byte level = 255)
    {
        var page = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        page.Erase(new SKColor(level, level, level));
        return page;
    }

    // Rows of word-sized blocks, drawn turned clockwise by lean degrees about the centre.
    public static SKBitmap Ruled(double lean = 0, int width = 600, int height = 800)
    {
        var page = Blank(width, height);
        using var canvas = new SKCanvas(page);
        canvas.RotateDegrees((float)lean, width / 2f, height / 2f);
        using var paint = new SKPaint { Color = SKColors.Black };
        for (var y = height / 10; y < height * 9 / 10; y += 24)
            for (var x = width / 10; x < width * 9 / 10; x += 40)
                canvas.DrawRect(x, y, 28, 8, paint);
        return page;
    }

    // A sheet printed both sides: the front solid on the left, the reverse showing through
    // faintly and mirrored on the right.
    public static SKBitmap Doubled(byte ghost = 205)
    {
        var page = Blank();
        using var canvas = new SKCanvas(page);
        using var reverse = new SKPaint { Color = new SKColor(ghost, ghost, ghost) };
        for (var y = 70; y < 730; y += 24)
            for (var x = 330; x < 560; x += 40)
                canvas.DrawRect(x, y, 26, 7, reverse);
        using var front = new SKPaint { Color = new SKColor(20, 20, 20) };
        for (var y = 60; y < 740; y += 24)
            for (var x = 40; x < 300; x += 40)
                canvas.DrawRect(x, y, 28, 8, front);
        return page;
    }

    public static (int Left, int Right) Ink(SKBitmap page, int split = 310)
    {
        int left = 0, right = 0;
        for (var y = 0; y < page.Height; y++)
            for (var x = 0; x < page.Width; x++)
            {
                if (page.GetPixel(x, y).Red >= 250) continue;
                if (x < split) left++; else right++;
            }
        return (left, right);
    }

    public static int Dark(SKBitmap page) => Ink(page, int.MaxValue).Left;

    public static SKBitmap Of(Umsatzschaetzung.Model.Raster raster)
    {
        var page = new SKBitmap(new SKImageInfo(raster.Width, raster.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        System.Runtime.InteropServices.Marshal.Copy(raster.Pixels, 0, page.GetPixels(), raster.Pixels.Length);
        return page;
    }

    public static byte[] Png(SKBitmap page)
    {
        using var data = page.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

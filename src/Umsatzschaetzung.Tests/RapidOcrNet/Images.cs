using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

static class Images
{
    public static SKBitmap Blank(int width, int height, SKColor? color = null)
    {
        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque));
        bitmap.Erase(color ?? SKColors.White);
        return bitmap;
    }

    public static void Fill(SKBitmap bitmap, int x0, int y0, int x1, int y1, SKColor color)
    {
        for (var y = y0; y < y1; y++)
            for (var x = x0; x < x1; x++)
                bitmap.SetPixel(x, y, color);
    }

    // Each pixel its own colour, so a moved pixel is recognised wherever it lands.
    public static SKBitmap Coded(int width, int height)
    {
        var bitmap = Blank(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                bitmap.SetPixel(x, y, Code(x, y));
        return bitmap;
    }

    public static SKColor Code(int x, int y) => new((byte)(40 + 50 * x), (byte)(30 + 70 * y), (byte)(200 - 20 * x - 30 * y));

    public static SKBitmap Text(int width, int height, float size, params (string Text, float X, float Baseline)[] lines)
    {
        var bitmap = Blank(width, height);
        using var canvas = new SKCanvas(bitmap);
        using var typeface = SKTypeface.FromFamilyName("Arial") ?? SKTypeface.Default;
        using var font = new SKFont(typeface, size);
        using var paint = new SKPaint { Color = SKColors.Black, IsAntialias = true };
        foreach (var (text, x, baseline) in lines)
            canvas.DrawText(text, x, baseline, font, paint);
        return bitmap;
    }

    public static SKBitmap Turned180(SKBitmap source)
    {
        var turned = Blank(source.Width, source.Height);
        using var canvas = new SKCanvas(turned);
        canvas.Translate(source.Width, source.Height);
        canvas.RotateDegrees(180);
        canvas.DrawBitmap(source, 0, 0);
        return turned;
    }

    public static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1], previous[j]) + 1, previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    public static (int X0, int Y0, int X1, int Y1) Bounds(SKPointI[] quad) =>
        (quad.Min(p => p.X), quad.Min(p => p.Y), quad.Max(p => p.X), quad.Max(p => p.Y));
}

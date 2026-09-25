using SkiaSharp;

namespace RapidOcrNet;

// A page-sized resize drawn in bands of rows, one core each. The bands differ only in their
// clip, so every pixel is sampled where SKBitmap.Resize samples it and comes out the same.
public static class Bands
{
    const int Rows = 64;

    public static SKBitmap Resize(SKBitmap source, SKImageInfo info, SKSamplingOptions sampling)
    {
        var resized = new SKBitmap(info);
        using var image = SKImage.FromPixels(source.PeekPixels());
        var scale = SKMatrix.CreateScale((float)info.Width / source.Width, (float)info.Height / source.Height);
        var pixels = resized.GetPixels();
        var rowBytes = resized.RowBytes;
        Parallel.For(0, (info.Height + Rows - 1) / Rows, band =>
        {
            using var surface = SKSurface.Create(info, pixels, rowBytes);
            using var shader = image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, sampling, scale);
            using var paint = new SKPaint { Shader = shader, BlendMode = SKBlendMode.Src };
            surface.Canvas.ClipRect(SKRect.Create(0, band * Rows, info.Width, Rows));
            surface.Canvas.DrawPaint(paint);
        });
        resized.NotifyPixelsChanged();
        return resized;
    }
}

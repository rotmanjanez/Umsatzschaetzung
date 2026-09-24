using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;
using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

public static class Scan
{
    public const int Dpi = 300;

    public static async Task<List<OcrPage>> Read(IOcr? ocr, IPdfPages? pdf, string fileName, byte[] data, int dpi, CancellationToken ct)
    {
        if (ocr is null) throw new ServiceError(ErrorCode.Unsupported, "Texterkennung nicht verfügbar");
        var pages = new List<OcrPage>();
        switch (InvoiceParser.Detect(data))
        {
            case Kind.Image:
                pages.Add(await ocr.Recognize(data, ct));
                break;
            case Kind.Pdf or Kind.Zugferd:
                await foreach (var page in Rasterize(pdf, data, dpi, ct))
                    using (page)
                        pages.Add(await ocr.Recognize(page, ct));
                break;
            default:
                throw new ServiceError(ErrorCode.Unsupported, $"\"{fileName}\" ist kein Scan");
        }
        if (pages.Count == 0) throw new ServiceError(ErrorCode.Unsupported, $"keine Seiten in \"{fileName}\" gefunden");
        return pages;
    }

    // A fresh render of the document is brought into the frame the reading's boxes sit in.
    public static Raster Upright(SKBitmap page, Correction c)
    {
        var (map, size) = Frame(page.Width, page.Height, c);
        return Draw(page, map, c.Scale, new SKRectI(0, 0, size.Width, size.Height));
    }

    // Only the region is drawn, so a row costs its own pixels, not the page's.
    public static Raster? Cut(SKBitmap page, Correction c, SKRectI region)
    {
        var (map, size) = Frame(page.Width, page.Height, c);
        var clipped = SKRectI.Intersect(region, new SKRectI(0, 0, size.Width, size.Height));
        return clipped.Width > 0 && clipped.Height > 0 ? Draw(page, map, c.Scale, clipped) : null;
    }

    // The reader's steps as one transform, each on the size the one before left: scaled,
    // straightened, turned and straightened again. One resample instead of four.
    public static (SKMatrix Map, SKSizeI Size) Frame(int width, int height, Correction c)
    {
        var map = SKMatrix.Identity;
        var size = new SKSizeI(width, height);
        if (c.Scale != 1)
        {
            size = new SKSizeI(Math.Max((int)Math.Round(width * c.Scale), 1), Math.Max((int)Math.Round(height * c.Scale), 1));
            map = SKMatrix.CreateScale((float)size.Width / width, (float)size.Height / height);
        }
        if (c.Skew != 0) (map, size) = Then(map, Deskew.Straightening(size, c.Skew));
        if (c.Turn != 0) (map, size) = Then(map, RapidOcr.Turning(size, c.Turn));
        if (c.Settle != 0) (map, size) = Then(map, Deskew.Straightening(size, c.Settle));
        return (map, size);
    }

    static (SKMatrix, SKSizeI) Then(SKMatrix map, (SKMatrix Map, SKSizeI Size) step) => (step.Map.PreConcat(map), step.Size);

    // Drawn straight into the array the view is handed.
    static Raster Draw(SKBitmap page, SKMatrix map, double scale, SKRectI region)
    {
        var info = new SKImageInfo(region.Width, region.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var pixels = GC.AllocateUninitializedArray<byte>(info.BytesSize, pinned: true);
        using (var target = new SKBitmap())
        {
            target.InstallPixels(info, Marshal.UnsafeAddrOfPinnedArrayElement(pixels, 0), info.RowBytes);
            using var canvas = new SKCanvas(target);
            canvas.Clear(SKColors.White);
            canvas.Translate(-region.Left, -region.Top);
            canvas.Concat(map);
            using var image = SKImage.FromPixels(page.PeekPixels());
            canvas.DrawImage(image, 0, 0, new SKSamplingOptions(SKFilterMode.Linear, scale < 1 ? SKMipmapMode.Linear : SKMipmapMode.None));
        }
        return new Raster(region.Width, region.Height, pixels);
    }

    // An image is its one page; a PDF is rendered at the given resolution.
    public static async IAsyncEnumerable<SKBitmap> Pages(IPdfPages? pdf, byte[] data, int dpi, [EnumeratorCancellation] CancellationToken ct)
    {
        if (InvoiceParser.Detect(data) == Kind.Image)
        {
            yield return await Task.Run(() => Decode(data), ct);
            yield break;
        }
        await foreach (var page in Rasterize(pdf, data, dpi, ct))
            yield return page;
    }

    public static Task<SKBitmap> Page(IPdfPages? pdf, byte[] data, int index, int dpi, CancellationToken ct) =>
        InvoiceParser.Detect(data) == Kind.Image
            ? index == 0 ? Task.Run(() => Decode(data), ct) : throw new InvalidDataException($"Bild hat keine Seite {index + 1}")
            : pdf?.Page(data, index, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");

    static SKBitmap Decode(byte[] image) =>
        SKBitmap.Decode(image) ?? throw new InvalidDataException("Das Seitenbild konnte nicht gelesen werden.");

    static IAsyncEnumerable<SKBitmap> Rasterize(IPdfPages? pdf, byte[] data, int dpi, CancellationToken ct) =>
        pdf?.Rasterize(data, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");
}

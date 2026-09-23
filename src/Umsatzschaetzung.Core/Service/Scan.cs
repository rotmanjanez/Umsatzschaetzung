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
    public static byte[] Upright(byte[] image, Correction c)
    {
        if (c is { Scale: 1, Skew: 0, Turn: 0, Settle: 0 }) return image;
        var page = RapidOcr.Decode(image);
        if (c.Scale != 1) page = Next(page, Scaled(page, c.Scale));
        if (c.Skew != 0) page = Next(page, Deskew.Straighten(page, c.Skew));
        if (c.Turn != 0) page = Next(page, RapidOcr.Rotate(page, c.Turn));
        if (c.Settle != 0) page = Next(page, Deskew.Straighten(page, c.Settle));
        using (page) return PdfiumPages.Png(page);
    }

    static SKBitmap Next(SKBitmap page, SKBitmap next)
    {
        page.Dispose();
        return next;
    }

    static SKBitmap Scaled(SKBitmap page, double scale)
    {
        var info = new SKImageInfo(Math.Max((int)Math.Round(page.Width * scale), 1), Math.Max((int)Math.Round(page.Height * scale), 1),
            SKColorType.Bgra8888, SKAlphaType.Premul);
        return page.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidOperationException("Das Seitenbild konnte nicht skaliert werden.");
    }

    public static Task<List<byte[]>> Render(IPdfPages? pdf, byte[] data, int dpi, CancellationToken ct) =>
        pdf?.Render(data, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");

    static IAsyncEnumerable<SKBitmap> Rasterize(IPdfPages? pdf, byte[] data, int dpi, CancellationToken ct) =>
        pdf?.Rasterize(data, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");
}

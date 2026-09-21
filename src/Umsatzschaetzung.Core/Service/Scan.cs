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

    public static Task<List<byte[]>> Render(IPdfPages? pdf, byte[] data, int dpi, CancellationToken ct) =>
        pdf?.Render(data, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");

    static IAsyncEnumerable<SKBitmap> Rasterize(IPdfPages? pdf, byte[] data, int dpi, CancellationToken ct) =>
        pdf?.Rasterize(data, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");
}

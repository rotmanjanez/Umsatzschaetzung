using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

public static class Scan
{
    public const int Dpi = 300;

    public static async Task<List<OcrPage>> Read(IOcr? ocr, IPdfPages? pdf, string fileName, byte[] data, int dpi, CancellationToken ct)
    {
        if (ocr is null) throw new ServiceError(ErrorCode.Unsupported, "Texterkennung nicht verfügbar");
        var images = InvoiceParser.Detect(data) switch
        {
            Kind.Image => [data],
            Kind.Pdf or Kind.Zugferd => await Render(pdf, data, dpi, ct),
            _ => throw new ServiceError(ErrorCode.Unsupported, $"\"{fileName}\" ist kein Scan"),
        };
        if (images.Count == 0) throw new ServiceError(ErrorCode.Unsupported, $"keine Seiten in \"{fileName}\" gefunden");
        var pages = new List<OcrPage>(images.Count);
        foreach (var image in images)
        {
            var page = await ocr.Recognize(image, ct);
            if (page.Image.Length == 0) page.Image = image;
            pages.Add(page);
        }
        return pages;
    }

    public static Task<List<byte[]>> Render(IPdfPages? pdf, byte[] data, int dpi, CancellationToken ct) =>
        pdf?.Render(data, dpi, ct) ?? throw new ServiceError(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");
}

using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

// Image stays empty when the page was read as delivered; a cleaned, straightened or
// turned page comes back encoded, since its boxes sit in that frame.
public interface IOcr
{
    Task<OcrPage> Recognize(byte[] image, CancellationToken ct);
}

public interface IPdfPages
{
    Task<List<byte[]>> Render(byte[] pdf, int dpi, CancellationToken ct);
}

public interface IPdfPrinter
{
    Task<byte[]> Print(string html, CancellationToken ct);
}

public static class PdfRaster
{
    // A scan wrapped one point per pixel reports A4 as 3307 x 4677 points, so 300 dpi would
    // render 10333 px wide: a 600 MB bitmap with no more detail than the scan inside it.
    public static (int Width, int Height) Target(double width, double height, double scale)
    {
        var longest = Math.Max(width, height) * scale;
        if (longest > RapidOcr.MaxImageDimension) scale *= RapidOcr.MaxImageDimension / longest;
        return (Round(width * scale), Round(height * scale));
    }

    static int Round(double v) => Math.Max(1, (int)Math.Round(v));
}

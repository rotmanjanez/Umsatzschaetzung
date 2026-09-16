using Umsatzschätzung.Model;

namespace Umsatzschätzung.Service;

public sealed record OcrPageWords(int Width, int Height, List<OcrWord> Words, byte[]? Image = null);

public interface IOcr
{
    Task<OcrPageWords> Recognize(byte[] image, CancellationToken ct);
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
    // A scan wrapped one point per pixel reports an A4 page as 3307 x 4677 device
    // units instead of 794 x 1123, so scaling it to 300 dpi renders it at 10333 px
    // wide: a 600 MB bitmap holding no more detail than the 2480 px scan inside it.
    // The recogniser reads nothing above MaxImageDimension anyway, so stop there.
    public static (int Width, int Height) Target(double width, double height, double scale)
    {
        var longest = Math.Max(width, height) * scale;
        if (longest > RapidOcr.MaxImageDimension) scale *= RapidOcr.MaxImageDimension / longest;
        return (Round(width * scale), Round(height * scale));
    }

    static int Round(double v) => Math.Max(1, (int)Math.Round(v));
}

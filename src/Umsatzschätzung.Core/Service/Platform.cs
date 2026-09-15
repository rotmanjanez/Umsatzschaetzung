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

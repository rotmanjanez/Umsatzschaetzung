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

public sealed record LlmRequest(string System, string User, string Grammar, int MaxTokens);

public interface ILlmEngine : IDisposable
{
    string Model { get; }
    Task<string> Complete(LlmRequest request, CancellationToken ct);
}

public sealed record LlmStats(int PromptTokens, int PromptDone, int GenTokens, double PromptMs, double GenMs);

// Raised a few times a second while a completion runs, from the thread running it.
public interface ILlmProgress
{
    event Action<LlmStats>? Progress;
}

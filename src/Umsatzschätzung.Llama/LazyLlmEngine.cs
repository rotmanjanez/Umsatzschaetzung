using Umsatzschätzung.Service;

namespace Umsatzschätzung.Llama;

// Loading the weights takes seconds and pulls in the native library, so it happens on the
// first completion instead of at startup: a failure then surfaces on the request that
// needed the model rather than silently disabling recognition before the window is up.
public sealed class LazyLlmEngine(string modelDir) : ILlmEngine, ILlmProgress
{
    readonly SemaphoreSlim gate = new(1, 1);
    LlamaEngine? engine;
    bool disposed;

    public string Model => LlamaEngine.ModelId;

    public event Action<LlmStats>? Progress;

    public async Task<string> Complete(LlmRequest request, CancellationToken ct) =>
        await (await Engine(ct)).Complete(request, ct);

    async Task<LlamaEngine> Engine(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (engine is null)
            {
                var opened = await Task.Run(() => new LlamaEngine(modelDir), ct);
                opened.Progress += OnProgress;
                engine = opened;
            }
            return engine;
        }
        finally
        {
            gate.Release();
        }
    }

    void OnProgress(LlmStats stats) => Progress?.Invoke(stats);

    public void Dispose()
    {
        gate.Wait();
        try
        {
            disposed = true;
            engine?.Dispose();
            engine = null;
        }
        finally
        {
            gate.Release();
        }
    }
}

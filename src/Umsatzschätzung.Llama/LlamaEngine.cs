using System.IO;
using System.Runtime.InteropServices;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.Llama;

public sealed class LlamaEngine : ILlmEngine
{
    public const string ModelFile = "gemma-4-E2B_q4_0-it-00001-of-00003.gguf";
    const int Context = 8192;
    const int DefaultMaxTokens = 512;
    const int Aborted = 1;

    readonly SemaphoreSlim gate = new(1, 1);
    IntPtr handle;

    public LlamaEngine(string modelDir)
    {
        handle = Native.Open(Path.Combine(modelDir, ModelFile), Context, Environment.ProcessorCount, out var err);
        if (handle == IntPtr.Zero)
            throw new InvalidOperationException($"Sprachmodell {ModelFile} konnte nicht geladen werden: {Take(err)}");
    }

    public string Model => "gemma-4-e2b";

    public async Task<string> Complete(LlmRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await gate.WaitAsync(ct);
        try
        {
            if (handle == IntPtr.Zero)
                throw new ObjectDisposedException(nameof(LlamaEngine));
            var prompt = Render(request.System, request.User);
            var grammar = request.Grammar.Length > 0 ? request.Grammar : null;
            var maxTokens = request.MaxTokens > 0 ? request.MaxTokens : DefaultMaxTokens;
            var h = handle;
            var text = await Task.Run(() =>
            {
                using var abort = ct.Register(() => Native.Abort(h));
                var rc = Native.Complete(h, prompt, grammar, maxTokens, out var outPtr, out var errPtr);
                var output = Take(outPtr);
                var err = Take(errPtr);
                if (rc == Aborted || ct.IsCancellationRequested)
                    throw new OperationCanceledException(ct);
                if (rc != 0)
                    throw new InvalidOperationException($"Generierung fehlgeschlagen ({rc}): {err}");
                return output;
            }, ct);
            var i = text.LastIndexOf("</think>", StringComparison.Ordinal);
            return (i >= 0 ? text[(i + "</think>".Length)..] : text).Trim();
        }
        finally
        {
            gate.Release();
        }
    }

    static string Render(string system, string user) =>
        "<|turn>system\n" + system.Trim() + "<turn|>\n<|turn>user\n" + user.Trim() + "<turn|>\n<|turn>model\n";

    static string Take(IntPtr p)
    {
        if (p == IntPtr.Zero)
            return "";
        var s = Marshal.PtrToStringUTF8(p) ?? "";
        Native.Free(p);
        return s;
    }

    public void Dispose()
    {
        gate.Wait();
        try
        {
            if (handle != IntPtr.Zero)
                Native.Close(handle);
            handle = IntPtr.Zero;
        }
        finally
        {
            gate.Release();
        }
    }

    static class Native
    {
        const string Lib = "umsatzschaetzung_llm";

        [DllImport(Lib, EntryPoint = "umsatzschaetzung_llm_open", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr Open([MarshalAs(UnmanagedType.LPUTF8Str)] string modelPath, int nCtx, int nThreads, out IntPtr err);

        [DllImport(Lib, EntryPoint = "umsatzschaetzung_llm_complete", CallingConvention = CallingConvention.Cdecl)]
        public static extern int Complete(IntPtr h, [MarshalAs(UnmanagedType.LPUTF8Str)] string prompt,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string? grammar, int maxTokens, out IntPtr output, out IntPtr err);

        [DllImport(Lib, EntryPoint = "umsatzschaetzung_llm_abort", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Abort(IntPtr h);

        [DllImport(Lib, EntryPoint = "umsatzschaetzung_llm_close", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Close(IntPtr h);

        [DllImport(Lib, EntryPoint = "umsatzschaetzung_llm_free", CallingConvention = CallingConvention.Cdecl)]
        public static extern void Free(IntPtr p);
    }
}

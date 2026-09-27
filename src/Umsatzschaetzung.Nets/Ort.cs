using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.OnnxRuntime.EP.WebGpu;

namespace Umsatzschaetzung.Nets;

// The native runtime over the weights in a folder. A run returns finished: the callers are on
// the pool already.
public sealed class OrtWeights(string dir) : IWeights
{
    public bool Accelerated => Accelerator.Available;

    public Task<INet> Open(string model, NetOptions options, CancellationToken ct = default)
    {
        var path = Path.Combine(dir, model);
        if (!File.Exists(path)) throw new FileNotFoundException($"Modelldatei fehlt: '{path}'.", path);
        using var so = Options(options);
        var gate = options.Accelerated && Accelerator.Available ? Accelerator.Gate : null;
        if (gate is null) return Task.FromResult<INet>(new OrtNet(new InferenceSession(path, so), null));
        lock (gate) return Task.FromResult<INet>(new OrtNet(new InferenceSession(path, so), gate));
    }

    public Task<byte[]> Read(string file, CancellationToken ct = default) => File.ReadAllBytesAsync(Path.Combine(dir, file), ct);

    static SessionOptions Options(NetOptions o)
    {
        var so = new SessionOptions { IntraOpNumThreads = o.Threads, InterOpNumThreads = o.InterThreads };
        if (o.Extended) so.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
        if (!o.Spin) so.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
        if (o.Accelerated) Accelerator.Append(so);
        return so;
    }
}

sealed class OrtNet : INet
{
    readonly InferenceSession session;
    readonly object? gate;
    readonly string[] inputNames, outputNames;

    public IReadOnlyList<Port> Inputs { get; }
    public IReadOnlyList<Port> Outputs { get; }

    public OrtNet(InferenceSession session, object? gate)
    {
        this.session = session;
        this.gate = gate;
        inputNames = [.. session.InputNames];
        outputNames = [.. session.OutputNames];
        Inputs = [.. inputNames.Select(n => new Port(n, session.InputMetadata[n].Dimensions))];
        Outputs = [.. outputNames.Select(n => new Port(n, session.OutputMetadata[n].Dimensions))];
    }

    public Task<Tensor[]> Run(IReadOnlyList<Tensor> inputs, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var values = inputs.Select(Value).ToArray();
        try
        {
            using var options = new RunOptions();
            var names = inputs.Select(t => t.Name).ToArray();
            IDisposableReadOnlyCollection<OrtValue> results;
            if (gate is null) results = session.Run(options, names, values, outputNames);
            else lock (gate) results = session.Run(options, names, values, outputNames);
            using (results) return Task.FromResult(outputNames.Select((n, i) => Read(n, results[i])).ToArray());
        }
        finally
        {
            foreach (var v in values) v.Dispose();
        }
    }

    public void Dispose() => session.Dispose();

    static OrtValue Value(Tensor t)
    {
        var shape = Array.ConvertAll(t.Shape, d => (long)d);
        return t.Floats is { } f ? OrtValue.CreateTensorValueFromMemory(f, shape) : OrtValue.CreateTensorValueFromMemory(t.Longs!, shape);
    }

    static Tensor Read(string name, OrtValue value)
    {
        var info = value.GetTensorTypeAndShape();
        var shape = Array.ConvertAll(info.Shape, d => (int)d);
        return info.ElementDataType == TensorElementType.Int64
            ? Tensor.Of(name, value.GetTensorDataAsSpan<long>().ToArray(), shape)
            : Tensor.Of(name, value.GetTensorDataAsSpan<float>().ToArray(), shape);
    }
}

// The WebGPU plugin: DirectX 12 on Windows, Metal on macOS, loaded beside the CPU runtime
// the tagger is pinned to. No adapter, no library, or a runtime that refuses it all mean CPU,
// and so does UMSATZSCHAETZUNG_CPU=1 for machines whose only adapter is a software one.
// The device takes one session at a time, at load and per run: the corpus tool runs a
// reader per core and they all queue here for the detector.
static class Accelerator
{
    static readonly Lazy<OrtEpDevice?> device = new(Find);

    public static readonly object Gate = new();

    public static bool Available => device.Value is not null;

    // The device's buffer cache keeps every page size it has seen, gigabytes of them in unified
    // memory; without it a page reads as fast.
    public static void Append(SessionOptions options)
    {
        if (device.Value is { } gpu) options.AppendExecutionProvider(OrtEnv.Instance(), [gpu], new Dictionary<string, string> { ["storageBufferCacheMode"] = "disabled" });
    }

    static OrtEpDevice? Find()
    {
        if (Environment.GetEnvironmentVariable("UMSATZSCHAETZUNG_CPU") == "1") return null;
        try
        {
            var env = OrtEnv.Instance();
            env.RegisterExecutionProviderLibrary("webgpu", WebGpuEp.GetLibraryPath());
            return env.GetEpDevices().FirstOrDefault(d => d.EpName == WebGpuEp.GetEpName());
        }
        catch (Exception e) when (e is OnnxRuntimeException or IOException or PlatformNotSupportedException or DllNotFoundException)
        {
            return null;
        }
    }
}

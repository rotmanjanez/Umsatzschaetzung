namespace Umsatzschaetzung.Nets;

// Row-major, as a runtime takes and gives it: floats or int64, never both.
public sealed record Tensor(string Name, int[] Shape, float[]? Floats = null, long[]? Longs = null)
{
    public static Tensor Of(string name, float[] data, params int[] shape) => new(name, shape, Floats: data);
    public static Tensor Of(string name, long[] data, params int[] shape) => new(name, shape, Longs: data);

    public float[] F => Floats ?? throw new InvalidOperationException($"\"{Name}\" hält keine Gleitkommazahlen.");
}

// A dimension of -1 is left to the input.
public sealed record Port(string Name, int[] Shape);

public interface INet : IDisposable
{
    IReadOnlyList<Port> Inputs { get; }
    IReadOnlyList<Port> Outputs { get; }

    // Outputs in the order of Outputs.
    Task<Tensor[]> Run(IReadOnlyList<Tensor> inputs, CancellationToken ct = default);
}

// Threads and InterThreads are the CPU runtime's, 0 its default; Extended stops the graph
// optimiser at the extended level. Accelerated asks for the GPU where there is one, a run at a time.
public sealed record NetOptions(int Threads = 0, int InterThreads = 1, bool Accelerated = false, bool Spin = true, bool Extended = false);

// Where the weights come from and what runs them. Names are as under models/: "v6/PP-OCRv6_det_small.onnx".
public interface IWeights
{
    bool Accelerated { get; }

    Task<INet> Open(string model, NetOptions options, CancellationToken ct = default);

    Task<byte[]> Read(string file, CancellationToken ct = default);
}

public static class Outputs
{
    public static Tensor Named(this Tensor[] outputs, string name) =>
        Array.Find(outputs, t => t.Name == name) ?? throw new InvalidOperationException($"Das Modell liefert keinen Ausgang \"{name}\".");
}

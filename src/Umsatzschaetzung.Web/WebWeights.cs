using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Umsatzschaetzung.Nets;

namespace Umsatzschaetzung.Web;

// onnxruntime-web in the page: WebGPU where the browser has an adapter and a model asks for
// it, WebAssembly otherwise. Inputs are staged and outputs taken straight from and into the
// arrays here, so a tensor is copied once each way.
public sealed partial class WebWeights : IWeights
{
    WebWeights(bool accelerated) => Accelerated = accelerated;

    public bool Accelerated { get; }

    public static async Task<WebWeights> Start() => new(await StartJs());

    public async Task<INet> Open(string model, NetOptions options, CancellationToken ct = default)
    {
        var info = JsonSerializer.Deserialize(await OpenJs(model, options.Accelerated), WebJson.Default.Opened)!;
        return new WebNet(info.Id, info.Inputs.Select(Port).ToArray(), info.Outputs.Select(Port).ToArray());
    }

    public async Task<byte[]> Read(string file, CancellationToken ct = default)
    {
        var handle = await ReadJs(file);
        var data = new byte[Length(handle)];
        Copy(handle, data);
        return data;
    }

    static Port Port(WebPort p) => new(p.Name, p.Shape);

    [JSImport("start", "nets")]
    private static partial Task<bool> StartJs();

    [JSImport("open", "nets")]
    private static partial Task<string> OpenJs(string model, bool accelerated);

    [JSImport("read", "nets")]
    private static partial Task<int> ReadJs(string file);

    [JSImport("length", "nets")]
    private static partial int Length(int handle);

    [JSImport("copy", "nets")]
    private static partial void Copy(int handle, [JSMarshalAs<JSType.MemoryView>] Span<byte> into);
}

sealed partial class WebNet(int id, Port[] inputs, Port[] outputs) : INet
{
    public IReadOnlyList<Port> Inputs => inputs;
    public IReadOnlyList<Port> Outputs => outputs;

    public async Task<Tensor[]> Run(IReadOnlyList<Tensor> feeds, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        foreach (var t in feeds)
            if (t.Floats is { } f) Stage(id, t.Name, "float32", t.Shape, MemoryMarshal.AsBytes(f.AsSpan()));
            else Stage(id, t.Name, "int64", t.Shape, MemoryMarshal.AsBytes(t.Longs.AsSpan()));
        var ran = JsonSerializer.Deserialize(await RunJs(id), WebJson.Default.Ran)!;
        try
        {
            var result = new Tensor[ran.Outputs.Length];
            for (var i = 0; i < result.Length; i++)
            {
                var o = ran.Outputs[i];
                var count = o.Shape.Aggregate(1, (a, d) => a * d);
                if (o.Type == "int64")
                {
                    var data = new long[count];
                    Take(ran.Handle, i, MemoryMarshal.AsBytes(data.AsSpan()));
                    result[i] = Tensor.Of(o.Name, data, o.Shape);
                }
                else
                {
                    var data = new float[count];
                    Take(ran.Handle, i, MemoryMarshal.AsBytes(data.AsSpan()));
                    result[i] = Tensor.Of(o.Name, data, o.Shape);
                }
            }
            return result;
        }
        finally
        {
            Drop(ran.Handle);
        }
    }

    public void Dispose() => Close(id);

    [JSImport("stage", "nets")]
    private static partial void Stage(int id, string name, string type, int[] shape, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("run", "nets")]
    private static partial Task<string> RunJs(int id);

    [JSImport("take", "nets")]
    private static partial void Take(int handle, int index, [JSMarshalAs<JSType.MemoryView>] Span<byte> into);

    [JSImport("drop", "nets")]
    private static partial void Drop(int handle);

    [JSImport("close", "nets")]
    private static partial void Close(int id);
}

sealed record WebPort(string Name, int[] Shape);
sealed record Opened(int Id, WebPort[] Inputs, WebPort[] Outputs);
sealed record Output(string Name, string Type, int[] Shape);
sealed record Ran(int Handle, Output[] Outputs);

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[System.Text.Json.Serialization.JsonSerializable(typeof(Opened))]
[System.Text.Json.Serialization.JsonSerializable(typeof(Ran))]
sealed partial class WebJson : System.Text.Json.Serialization.JsonSerializerContext;

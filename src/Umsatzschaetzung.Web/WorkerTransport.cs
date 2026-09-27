using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Web;

// The page's calls to the services in their worker. Attachments are handed over as they are
// staged, the answer's taken straight into arrays here.
public sealed partial class WorkerTransport : ITransport
{
    int calls;

    public static Task Start() => StartJs();

    public async Task<Message> Call(string method, Message request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var id = ++calls;
        foreach (var b in request.Blobs) Attach(b);
        using var cancel = ct.Register(() => Cancel(id));
        try
        {
            await CallJs(id, method, request.Json);
        }
        catch (JSException) when (ct.IsCancellationRequested)
        {
            throw new OperationCanceledException(ct);
        }
        try
        {
            var blobs = new byte[Count(id)][];
            for (var i = 0; i < blobs.Length; i++)
            {
                blobs[i] = new byte[Length(id, i)];
                Take(id, i, blobs[i]);
            }
            return new(Json(id), blobs, FaultJs(id) is { } f ? JsonSerializer.Deserialize(f, ServeJson.Default.Fault) : null);
        }
        finally
        {
            Done(id);
        }
    }

    [JSImport("start", "service")]
    private static partial Task StartJs();

    [JSImport("attach", "service")]
    private static partial void Attach([JSMarshalAs<JSType.MemoryView>] Span<byte> data);

    [JSImport("call", "service")]
    private static partial Task CallJs(int id, string method, string json);

    [JSImport("cancel", "service")]
    private static partial void Cancel(int id);

    [JSImport("json", "service")]
    private static partial string Json(int id);

    [JSImport("fault", "service")]
    private static partial string? FaultJs(int id);

    [JSImport("count", "service")]
    private static partial int Count(int id);

    [JSImport("length", "service")]
    private static partial int Length(int id, int index);

    [JSImport("take", "service")]
    private static partial void Take(int id, int index, [JSMarshalAs<JSType.MemoryView>] Span<byte> into);

    [JSImport("done", "service")]
    private static partial void Done(int id);
}

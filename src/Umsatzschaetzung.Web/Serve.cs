using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using System.Text.Json.Serialization;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Web;

// The worker's side: the page's calls answered by the services composed there.
public static partial class Serve
{
    static Dispatch? dispatch;
    static readonly Dictionary<int, CancellationTokenSource> running = [];

    // Everything the program asks for, over /work and the weights.
    public static async Task Start()
    {
        var weights = await WebWeights.Start();
        var rules = new RuleStore("/work/rules", RuleStore.Seed());
        dispatch = new(Services.Local(rules, new CaseStore("/work/cases"), Release.Version,
            documents: new Documents(new RapidOcr(weights), new PdfiumPages()),
            tagger: new Tagger(weights),
            ranking: new Prewarmed(weights, rules.Dir, () => new EncoderRanking(new Encoder(weights), new EmbeddingStore(rules.Dir)))));
    }

    [JSExport]
    public static async Task<string> Handle(int id, string method, string json, int count)
    {
        var blobs = new byte[count][];
        for (var i = 0; i < count; i++)
        {
            blobs[i] = new byte[Length(id, i)];
            Blob(id, i, blobs[i]);
        }
        using var cts = new CancellationTokenSource();
        running[id] = cts;
        try
        {
            var answer = await dispatch!.Handle(method, new(json, blobs), cts.Token);
            foreach (var b in answer.Blobs) Attach(id, b);
            return JsonSerializer.Serialize(new Reply(answer.Json, answer.Fault, false), ServeJson.Default.Reply);
        }
        catch (OperationCanceledException)
        {
            return JsonSerializer.Serialize(new Reply(null, null, true), ServeJson.Default.Reply);
        }
        finally
        {
            running.Remove(id);
        }
    }

    [JSExport]
    public static void Cancel(int id)
    {
        if (running.TryGetValue(id, out var cts)) cts.Cancel();
    }

    [JSImport("blob", "service")]
    private static partial void Blob(int id, int index, [JSMarshalAs<JSType.MemoryView>] Span<byte> into);

    [JSImport("length", "service")]
    private static partial int Length(int id, int index);

    [JSImport("attach", "service")]
    private static partial void Attach(int id, [JSMarshalAs<JSType.MemoryView>] Span<byte> data);
}

sealed record Reply(string? Json, Fault? Fault, bool Canceled);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Reply))]
[JsonSerializable(typeof(Fault))]
sealed partial class ServeJson : JsonSerializerContext;

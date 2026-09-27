using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

// A call or its answer as it crosses to another runtime: the JSON, and every byte[] in it beside
// the text instead of spelled out in it, so a scan or a page's pixels are handed over whole.
public sealed record Message(string Json, IReadOnlyList<byte[]> Blobs, Fault? Fault = null)
{
    public static readonly Message Empty = new("null", []);
}

public sealed record Fault(ErrorCode Code, string Message, List<string>? Details);

// Carries a call to the runtime that serves it; a call cancelled here is cancelled there.
public interface ITransport
{
    Task<Message> Call(string method, Message request, CancellationToken ct);
}

public sealed record CaseArg(string CaseId);
public sealed record FileArg(string FileName, byte[] Data);
public sealed record DataArg(byte[] Data);
public sealed record YearArg(int Year);
public sealed record CaseImportArg(string FileName, byte[] Data, bool Overwrite);
public sealed record CaseFileArg(string CaseId, string FileName, byte[] Data);
public sealed record InvoiceArg(string CaseId, string InvoiceId);
public sealed record SnippetArg(string CaseId, string InvoiceId, int Line, string Name);
public sealed record UnifyArg(string CaseId, List<string> InvoiceIds);
public sealed record SuggestArg(string CaseId, InvoiceLine Line, string? Supplier);
public sealed record RenderArg(string CaseId, bool Pdf);
public sealed record DeleteRuleArg(Entity Kind, string Id);

// One of them is set.
public sealed record RuleArg(Category? Category = null, Gewerbezweig? Gewerbezweig = null, Ingredient? Ingredient = null,
    ArticleMapping? Mapping = null, Product? Product = null, YieldRule? Yield = null, ReportTemplate? Template = null)
{
    public static RuleArg Of(IRuleEntity rule) => rule switch
    {
        Category c => new(Category: c),
        Gewerbezweig g => new(Gewerbezweig: g),
        Ingredient i => new(Ingredient: i),
        ArticleMapping m => new(Mapping: m),
        Product p => new(Product: p),
        YieldRule y => new(Yield: y),
        ReportTemplate t => new(Template: t),
        _ => throw new ServiceError(ErrorCode.Internal, $"Unbekannte Regel: {rule.GetType().Name}"),
    };

    public IRuleEntity Rule =>
        (IRuleEntity?)Category ?? (IRuleEntity?)Gewerbezweig ?? (IRuleEntity?)Ingredient ?? (IRuleEntity?)Mapping
        ?? (IRuleEntity?)Product ?? (IRuleEntity?)Yield ?? (IRuleEntity?)Template
        ?? throw new ServiceError(ErrorCode.Invalid, "Keine Regel übergeben");
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(CaseArg))]
[JsonSerializable(typeof(FileArg))]
[JsonSerializable(typeof(DataArg))]
[JsonSerializable(typeof(YearArg))]
[JsonSerializable(typeof(CaseImportArg))]
[JsonSerializable(typeof(CaseFileArg))]
[JsonSerializable(typeof(InvoiceArg))]
[JsonSerializable(typeof(SnippetArg))]
[JsonSerializable(typeof(UnifyArg))]
[JsonSerializable(typeof(SuggestArg))]
[JsonSerializable(typeof(RenderArg))]
[JsonSerializable(typeof(DeleteRuleArg))]
[JsonSerializable(typeof(RuleArg))]
[JsonSerializable(typeof(Case))]
[JsonSerializable(typeof(RuleSet))]
[JsonSerializable(typeof(VerifyReq))]
[JsonSerializable(typeof(StatusResp))]
[JsonSerializable(typeof(List<SammlungInfo>))]
[JsonSerializable(typeof(CasesResp))]
[JsonSerializable(typeof(ExportResp))]
[JsonSerializable(typeof(ParseResp))]
[JsonSerializable(typeof(OcrResp))]
[JsonSerializable(typeof(VerifyResp))]
[JsonSerializable(typeof(InvoiceSourceResp))]
[JsonSerializable(typeof(InvoiceReadingResp))]
[JsonSerializable(typeof(Raster))]
[JsonSerializable(typeof(AssortmentImport))]
[JsonSerializable(typeof(List<MappingCandidate>))]
[JsonSerializable(typeof(CalcResp))]
[JsonSerializable(typeof(ReportResp))]
sealed partial class WireContext : JsonSerializerContext;

public static class Wire
{
    static readonly JsonSerializerOptions Options = new(WireContext.Default.Options)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new BlobConverter() },
    };

    [ThreadStatic] static List<byte[]>? blobs;

    public static Message Write<T>(T value)
    {
        var into = blobs = [];
        try
        {
            return new(JsonSerializer.Serialize(value, Info<T>()), into);
        }
        finally
        {
            blobs = null;
        }
    }

    public static T Read<T>(Message m)
    {
        if (m.Fault is { } f) throw new ServiceError(f.Code, f.Message, f.Details);
        blobs = [.. m.Blobs];
        try
        {
            return JsonSerializer.Deserialize(m.Json, Info<T>())!;
        }
        finally
        {
            blobs = null;
        }
    }

    public static Message Fail(ServiceError e) => new("null", [], new Fault(e.Code, e.Message, e.Details as List<string>));

    static JsonTypeInfo<T> Info<T>() => (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T));

    sealed class BlobConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            blobs![reader.GetInt32()];

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            writer.WriteNumberValue(blobs!.Count);
            blobs.Add(value);
        }
    }
}

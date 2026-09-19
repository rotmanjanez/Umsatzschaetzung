using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(RuleSet))]
[JsonSerializable(typeof(Invoice))]
[JsonSerializable(typeof(Sammlung))]
public sealed partial class ModelJsonContext : JsonSerializerContext
{
}

public static class Json
{
    static readonly JsonSerializerOptions Options = new(ModelJsonContext.Default.Options) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, (JsonTypeInfo<T>)Options.GetTypeInfo(typeof(T)));

    public static T Deserialize<T>(ReadOnlySpan<byte> data) =>
        JsonSerializer.Deserialize(data, (JsonTypeInfo<T>)ModelJsonContext.Default.GetTypeInfo(typeof(T))!)
        ?? throw new JsonException("leeres Dokument");

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize(json, (JsonTypeInfo<T>)ModelJsonContext.Default.GetTypeInfo(typeof(T))!)
        ?? throw new JsonException("leeres Dokument");
}

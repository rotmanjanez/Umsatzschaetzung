using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Json.Serialization;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Model;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(RuleSet))]
[JsonSerializable(typeof(Case))]
[JsonSerializable(typeof(Invoice))]
[JsonSerializable(typeof(Sammlung))]
[JsonSerializable(typeof(OcrResp))]
[JsonSerializable(typeof(Source))]
[JsonSerializable(typeof(Field))]
[JsonSerializable(typeof(Sparte))]
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

    // Eine Datei speichert einen Aufzählungswert unter seinem JSON-Namen, nicht unter dem
    // C#-Bezeichner: der darf sich ändern, ohne dass eine gespeicherte Datei unlesbar wird.
    public static string Name<T>(T value) where T : struct, Enum => Names<T>.Of[value];

    public static T? Parse<T>(string name) where T : struct, Enum =>
        Names<T>.By.TryGetValue(name, out var value) ? value : null;

    static class Names<T> where T : struct, Enum
    {
        public static readonly Dictionary<T, string> Of = Enum.GetValues<T>().ToDictionary(v => v, v => Serialize(v).Trim('"'));
        public static readonly Dictionary<string, T> By = Of.ToDictionary(p => p.Value, p => p.Key, StringComparer.Ordinal);
    }

    public static T Copy<T>(T value) where T : class
    {
        var type = value.GetType();
        return (T)JsonSerializer.Deserialize(JsonSerializer.SerializeToUtf8Bytes(value, type, ModelJsonContext.Default), type, ModelJsonContext.Default)!;
    }
}

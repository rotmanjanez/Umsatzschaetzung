using System.Text.Json;
using System.Text.Json.Serialization;
using Umsatzschaetzung.Headless;

namespace Umsatzschaetzung.Lessons;

// The published app is trimmed, which leaves System.Text.Json without reflection.
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, AllowOutOfOrderMetadataProperties = true)]
[JsonSerializable(typeof(Goal))]
[JsonSerializable(typeof(Target))]
[JsonSerializable(typeof(List<Step>))]
[JsonSerializable(typeof(double[]))]
partial class LessonJson : JsonSerializerContext;

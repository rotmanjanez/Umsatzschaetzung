using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Umsatzschaetzung.Reports;

namespace Umsatzschaetzung;

// Writes the part of the template page that describes the data model, and its JSON Schema, from
// the very type the renderer serialises.
static class TemplateDoc
{
    public const string Begin = "<!-- generiert: Modell -->";
    public const string End = "<!-- /generiert -->";

    static JsonSerializerOptions Options => Html.JsonOptions;

    public static string Model()
    {
        var b = new StringBuilder();
        b.Append("## Filter\n\n| Filter | Wirkung |\n|---|---|\n");
        foreach (var (usage, help) in Template.FilterHelp) b.Append($"| `{usage}` | {help} |\n");

        List<Type> order = [typeof(ReportData)];
        HashSet<Type> seen = [typeof(ReportData)];
        for (var i = 0; i < order.Count; i++)
        {
            var rows = Options.GetTypeInfo(order[i]).Properties
                .Select(p => (p.Name, Type: Label(p.PropertyType, order, seen) + (p.IsGetNullable && !p.PropertyType.IsValueType ? "?" : ""), Help: Description(p)))
                .ToList();
            b.Append($"\n### {Title(order[i])} {{ #{Anchor(order[i])} }}\n\n");
            if (rows.Exists(r => r.Help != ""))
            {
                b.Append("| Feld | Typ | |\n|---|---|---|\n");
                foreach (var r in rows) b.Append($"| `{r.Name}` | {r.Type} | {r.Help} |\n");
            }
            else
            {
                b.Append("| Feld | Typ |\n|---|---|\n");
                foreach (var r in rows) b.Append($"| `{r.Name}` | {r.Type} |\n");
            }
        }
        return b.ToString();
    }

    public static string Schema()
    {
        var schema = Options.GetJsonSchemaAsNode(typeof(ReportData), new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (ctx, node) =>
            {
                if (node is JsonObject o && ctx.PropertyInfo is { } p && Description(p) is { Length: > 0 } d)
                    o.Insert(0, "description", d);
                return node;
            },
        });
        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = true, NewLine = "\n", Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) + "\n";
    }

    static string Description(JsonPropertyInfo p) =>
        p.AttributeProvider?.GetCustomAttributes(typeof(DescriptionAttribute), false)
            .OfType<DescriptionAttribute>().FirstOrDefault()?.Description ?? "";

    static string Label(Type t, List<Type> order, HashSet<Type> seen)
    {
        if (Nullable.GetUnderlyingType(t) is { } inner) return Label(inner, order, seen) + "?";
        if (t == typeof(string)) return "Text";
        if (t == typeof(bool)) return "ja/nein";
        if (t == typeof(int) || t == typeof(long)) return "Zahl";
        if (t == typeof(double)) return "Kommazahl";
        if (t == typeof(DateOnly)) return "Datum";
        if (t == typeof(DateTimeOffset)) return "Zeitpunkt";
        if (t.IsEnum) return string.Join(" \\| ", EnumNames(t).Select(n => $"`\"{n}\"`"));
        var info = Options.GetTypeInfo(t);
        if (info.Kind == JsonTypeInfoKind.Dictionary) return "ID → " + Label(info.ElementType!, order, seen);
        if (info.Kind == JsonTypeInfoKind.Enumerable) return "Liste von " + Label(info.ElementType!, order, seen);
        if (seen.Add(t)) order.Add(t);
        return $"[{Title(t)}](#{Anchor(t)})";
    }

    static IEnumerable<string> EnumNames(Type t) =>
        t.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(f => f.GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name ?? JsonNamingPolicy.CamelCase.ConvertName(f.Name));

    static string Title(Type t) => t == typeof(ReportData) ? "Wurzel" : t.Name;

    static string Anchor(Type t) => "m-" + t.Name.ToLowerInvariant();
}

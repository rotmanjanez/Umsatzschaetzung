using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Headless;

// Was ein Schritt anfasst: ein Name aus dem XAML, ein sichtbarer Text, ein
// Steuerelementtyp - und optional der Vorfahr, um den es eigentlich geht.
public sealed record Target
{
    public string? Name { get; init; }
    public string? Text { get; init; }
    public string? Type { get; init; }
    public string? Up { get; init; }
}

public sealed record Inset
{
    public double Top { get; init; }
    public double Right { get; init; }
    public double Bottom { get; init; }
    public double Left { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "do")]
[JsonDerivedType(typeof(ShotStep), "shot")]
[JsonDerivedType(typeof(ClickStep), "click")]
[JsonDerivedType(typeof(TypeStep), "type")]
[JsonDerivedType(typeof(FocusStep), "focus")]
[JsonDerivedType(typeof(DeselectStep), "deselect")]
[JsonDerivedType(typeof(TabStep), "tab")]
[JsonDerivedType(typeof(ImportStep), "import")]
[JsonDerivedType(typeof(WaitStep), "wait")]
public abstract record Step
{
    // "dialog" meint das zuletzt geöffnete Fenster über dem Hauptfenster.
    public string? Window { get; init; }
}

public sealed record ShotStep : Step
{
    public required string Name { get; init; }
    public Target? At { get; init; }
    public Inset? Trim { get; init; }
    // Unten am letzten Element dieses Typs abschneiden, etwa "DataGridRow".
    public string? Clip { get; init; }
}

public sealed record ClickStep : Step
{
    public required Target At { get; init; }
}

public sealed record TypeStep : Step
{
    public required Target At { get; init; }
    public required string Text { get; init; }
}

public sealed record FocusStep : Step
{
    public Target? At { get; init; }
}

public sealed record DeselectStep : Step
{
    public required Target At { get; init; }
}

public sealed record TabStep : Step
{
    public required string Header { get; init; }
}

public sealed record ImportStep : Step
{
    public required List<string> Files { get; init; }
}

public sealed record WaitStep : Step
{
    public int Rounds { get; init; } = 1;
}

// Ein Schritt je Zeile: so bleibt eine Zeile im Diff eine Handlung, und ein
// Skript lässt sich zusammensetzen, filtern und anhängen wie jede andere Liste.
public static class Steps
{
    static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IEnumerable<Step> Load(string path) =>
        File.ReadLines(path)
            .Select(l => l.Trim())
            .Where(l => l != "" && !l.StartsWith("//"))
            .Select(l => JsonSerializer.Deserialize<Step>(l, Options) ?? throw new JsonException(path + ": " + l));
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Headless;

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
[JsonDerivedType(typeof(SelectStep), "select")]
[JsonDerivedType(typeof(TopStep), "top")]
[JsonDerivedType(typeof(EditStep), "edit")]
[JsonDerivedType(typeof(OpenStep), "open")]
[JsonDerivedType(typeof(TabStep), "tab")]
[JsonDerivedType(typeof(ImportStep), "import")]
[JsonDerivedType(typeof(PickStep), "pick")]
[JsonDerivedType(typeof(ChooseStep), "choose")]
[JsonDerivedType(typeof(WaitStep), "wait")]
[JsonDerivedType(typeof(RestartStep), "restart")]
[JsonDerivedType(typeof(PressStep), "press")]
[JsonDerivedType(typeof(CloseStep), "close")]
public abstract record Step
{
    // "dialog" means the last window opened above the main window.
    public string? Window { get; init; }
    // With --trace, the step is sampled into <trace>.nettrace there.
    public string? Trace { get; init; }
}

public sealed record ShotStep : Step
{
    public required string Name { get; init; }
    public Target? At { get; init; }
    public Inset? Trim { get; init; }
    // Cut the bottom edge at the last element of this type, e.g. "DataGridRow".
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

public sealed record SelectStep : Step
{
    public required Target At { get; init; }
}

public sealed record TopStep : Step
{
    public required Target At { get; init; }
}

public sealed record EditStep : Step
{
    public required Target At { get; init; }
    public required string Column { get; init; }
    public string? Text { get; init; }
}

public sealed record OpenStep : Step
{
    public required string Number { get; init; }
}

public sealed record TabStep : Step
{
    public required string Header { get; init; }
}

public sealed record ImportStep : Step
{
    public required List<string> Files { get; init; }
}

public sealed record PickStep : Step
{
    public required List<string> Files { get; init; }
}

public sealed record ChooseStep : Step
{
    public required Target At { get; init; }
    public required string Text { get; init; }
    public required string Item { get; init; }
}

public sealed record WaitStep : Step
{
    public int Rounds { get; init; } = 1;
}

public sealed record RestartStep : Step;

public sealed record PressStep : Step
{
    public required Target At { get; init; }
}

public sealed record CloseStep : Step;

// One step per line: a line in the diff stays one action, and a script can be
// composed, filtered and appended to like any other list.
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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umsatzschätzung.Model;

public enum Unit
{
    [JsonStringEnumMemberName("ml")] Ml,
    [JsonStringEnumMemberName("g")] G,
    [JsonStringEnumMemberName("piece")] Piece,
}

public enum ValueUnit
{
    [JsonStringEnumMemberName("EUR")] Eur,
    [JsonStringEnumMemberName("ml")] Ml,
    [JsonStringEnumMemberName("g")] G,
    [JsonStringEnumMemberName("piece")] Piece,
    [JsonStringEnumMemberName("bp")] Bp,
    [JsonStringEnumMemberName("portion")] Portion,
}

public static class Bp
{
    public const long Full = 10000;
}

public sealed record UnitInfo(string Code, string Name, Unit Base, long Factor, string[] Aliases);

public static class Units
{
    // data/units.json ist die einzige Kopie; tools/units.py liest dieselbe Datei.
    static readonly UnitInfo[] All = Load();

    static readonly Dictionary<string, UnitInfo> Table =
        All.ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, string> Alias =
        (from u in All from a in u.Aliases select (a, u.Code))
        .ToDictionary(x => x.a, x => x.Code, StringComparer.OrdinalIgnoreCase);

    static UnitInfo[] Load()
    {
        using var stream = typeof(Units).Assembly.GetManifestResourceStream("units.json")
            ?? throw new InvalidOperationException("units.json fehlt in der Assembly.");
        using var json = JsonDocument.Parse(stream);
        return [.. json.RootElement.GetProperty("units").EnumerateArray().Select(u => new UnitInfo(
            u.GetProperty("code").GetString()!,
            u.GetProperty("name").GetString()!,
            u.GetProperty("base").GetString() switch { "ml" => Unit.Ml, "g" => Unit.G, _ => Unit.Piece },
            u.GetProperty("factor").GetInt64(),
            [.. u.GetProperty("aliases").EnumerateArray().Select(a => a.GetString()!)]))];
    }

    public static ValueUnit Value(Unit u) => u switch
    {
        Unit.Ml => ValueUnit.Ml,
        Unit.G => ValueUnit.G,
        _ => ValueUnit.Piece,
    };

    public static string Code(Unit u) => u switch
    {
        Unit.Ml => "ml",
        Unit.G => "g",
        _ => "piece",
    };

    public static string? Resolve(string text) => Alias.GetValueOrDefault(text);

    public static UnitInfo? Lookup(string code)
    {
        code = code.Trim();
        if (Table.TryGetValue(code, out var u)) return u;
        var alias = code.EndsWith('.') ? code[..^1] : code;
        return Alias.TryGetValue(alias, out var c) ? Table[c] : null;
    }

    public static string Label(string code) => Lookup(code)?.Name ?? code;
}

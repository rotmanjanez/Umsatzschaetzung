using System.Text.Json;
using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Model;

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

public sealed record UnitInfo(string Code, string Name, Unit Base, long Factor, bool Container, string[] Aliases);

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
            u.TryGetProperty("container", out var c) && c.GetBoolean(),
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

    static readonly Dictionary<char, char> Confusable = new()
    {
        ['1'] = 'l', ['i'] = 'l', ['|'] = 'l', ['0'] = 'o', ['5'] = 's', ['8'] = 'b', ['2'] = 'z', ['6'] = 'g', ['e'] = 'f',
    };

    static string Fold(string s) =>
        string.Concat(s.ToLowerInvariant().Select(c => Confusable.GetValueOrDefault(c, c)));

    // A folded form that two different codes could claim is dropped rather than guessed, so
    // a hit is unambiguous by construction. Today units.json produces none — NoFoldCollisions
    // holds that, and an alias added later degrades to no repair instead of a wrong one.
    static readonly Dictionary<string, string> FoldedAlias =
        (from u in All from a in u.Aliases group u.Code by Fold(a))
        .Where(g => g.Distinct().Count() == 1)
        .ToDictionary(g => g.Key, g => g.First());

    public static bool NoFoldCollisions => FoldedAlias.Count == Alias.Select(a => Fold(a.Key)).Distinct().Count();

    // Second attempt only, after Resolve missed: a bottle's "Fl" comes back as "F1" or "EI" and the
    // quantity column bleeds in as "17F1". Single characters stay out — "l" already resolves
    // exactly, so a lone digit here is a stray, not a litre.
    public static string? Unconfuse(string text)
    {
        var tail = text.AsSpan().TrimStart("0123456789.,;:-/ ").ToString();
        return tail.Length < 2 ? null : FoldedAlias.GetValueOrDefault(Fold(tail));
    }

    public static UnitInfo? Lookup(string code)
    {
        code = code.Trim();
        if (Table.TryGetValue(code, out var u)) return u;
        var alias = code.EndsWith('.') ? code[..^1] : code;
        return Alias.TryGetValue(alias, out var c) ? Table[c] : null;
    }

    public static string Label(string code) => Lookup(code)?.Name ?? code;
}

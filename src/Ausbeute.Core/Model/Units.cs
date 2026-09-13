using System.Text.Json.Serialization;

namespace Ausbeute.Model;

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

public sealed record UnitInfo(string Code, string Name, Unit Base, long Factor);

public static class Units
{
    static readonly Dictionary<string, UnitInfo> Table = new UnitInfo[]
    {
        new("KGM", "Kilogramm", Unit.G, 1000),
        new("GRM", "Gramm", Unit.G, 1),
        new("LTR", "Liter", Unit.Ml, 1000),
        new("MLT", "Milliliter", Unit.Ml, 1),
        new("CLT", "Zentiliter", Unit.Ml, 10),
        new("H87", "Stück", Unit.Piece, 1),
        new("C62", "Einheit", Unit.Piece, 1),
        new("PCE", "Stück", Unit.Piece, 1),
        new("EA", "Stück", Unit.Piece, 1),
        new("XBO", "Flasche", Unit.Piece, 1),
        new("XCT", "Karton", Unit.Piece, 1),
        new("XCS", "Kiste", Unit.Piece, 1),
        new("XBX", "Box", Unit.Piece, 1),
        new("XPK", "Packung", Unit.Piece, 1),
        new("XCR", "Kasten", Unit.Piece, 1),
        new("XBA", "Fass", Unit.Piece, 1),
        new("XKG", "Keg", Unit.Piece, 1),
        new("XBG", "Beutel", Unit.Piece, 1),
        new("XCI", "Kanister", Unit.Piece, 1),
        new("XSA", "Sack", Unit.Piece, 1),
        new("XCA", "Dose", Unit.Piece, 1),
        new("XRO", "Rolle", Unit.Piece, 1),
        new("XBH", "Bund", Unit.Piece, 1),
    }.ToDictionary(u => u.Code, StringComparer.OrdinalIgnoreCase);

    static readonly Dictionary<string, string> Alias = new(StringComparer.OrdinalIgnoreCase)
    {
        ["stk"] = "H87",
        ["st"] = "H87",
        ["stck"] = "H87",
        ["stück"] = "H87",
        ["stueck"] = "H87",
        ["pc"] = "H87",
        ["fl"] = "XBO",
        ["fla"] = "XBO",
        ["flasche"] = "XBO",
        ["flaschen"] = "XBO",
        ["kt"] = "XCT",
        ["karton"] = "XCT",
        ["kartons"] = "XCT",
        ["ki"] = "XCS",
        ["kiste"] = "XCS",
        ["kisten"] = "XCS",
        ["kasten"] = "XCR",
        ["fass"] = "XKG",
        ["fässer"] = "XKG",
        ["beutel"] = "XBG",
        ["btl"] = "XBG",
        ["kanister"] = "XCI",
        ["kan"] = "XCI",
        ["sack"] = "XSA",
        ["säcke"] = "XSA",
        ["sa"] = "XSA",
        ["pk"] = "XPK",
        ["pkg"] = "XPK",
        ["packung"] = "XPK",
        ["pack"] = "XPK",
        ["dose"] = "XCA",
        ["box"] = "XBX",
        ["rolle"] = "XRO",
        ["rollen"] = "XRO",
        ["bund"] = "XBH",
        ["ds"] = "XCA",
        ["kg"] = "KGM",
        ["g"] = "GRM",
        ["l"] = "LTR",
        ["lt"] = "LTR",
        ["ltr"] = "LTR",
        ["ml"] = "MLT",
        ["cl"] = "CLT",
    };

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

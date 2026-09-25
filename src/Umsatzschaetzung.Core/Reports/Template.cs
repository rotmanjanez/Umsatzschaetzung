using System.Text;
using System.Text.Json.Nodes;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Reports;

public sealed class TemplateError(string message) : Exception(message);

public static class Template
{
    public static void Check(string source)
    {
        var i = 0;
        Parse(source.ReplaceLineEndings("\n"), ref i, [], out _);
    }

    public static string Render(string source, JsonObject data)
    {
        var i = 0;
        var body = Parse(source.ReplaceLineEndings("\n"), ref i, [], out _);
        var b = new StringBuilder(64 * 1024);
        Write(b, body, data, []);
        return b.ToString();
    }

    abstract record Node;

    sealed record Text(string Value) : Node;

    sealed record Value(string Path, List<(string Name, string[] Args)> Filters) : Node;

    sealed record For(string Name, string Path, List<Node> Body) : Node;

    sealed record If(string Path, bool Negated, string? Literal, List<Node> Then, List<Node> Else) : Node;

    static List<Node> Parse(string s, ref int i, string[] until, out string? stopped)
    {
        stopped = null;
        List<Node> nodes = [];
        var text = new StringBuilder();
        while (i < s.Length)
        {
            var open = s.IndexOf('{', i);
            if (open < 0 || open + 1 >= s.Length || (s[open + 1] != '{' && s[open + 1] != '%'))
            {
                text.Append(s, i, (open < 0 ? s.Length : open + 1) - i);
                i = open < 0 ? s.Length : open + 1;
                continue;
            }
            text.Append(s, i, open - i);

            var block = s[open + 1] == '%';
            var close = s.IndexOf(block ? "%}" : "}}", open, StringComparison.Ordinal);
            if (close < 0) throw new TemplateError($"nicht geschlossen: {s.Substring(open, Math.Min(30, s.Length - open))}");
            var tag = s[(open + 2)..close].Trim();
            i = close + 2;

            if (!block)
            {
                Flush(nodes, text);
                var parts = tag.Split('|', StringSplitOptions.TrimEntries);
                var calls = parts.Skip(1).Select(f => f.Split(':')).ToList();
                if (calls.Exists(f => !Filters.ContainsKey(f[0])))
                    throw new TemplateError($"unbekannter Filter in {{{{ {tag} }}}}");
                nodes.Add(new Value(parts[0], calls.Select(f => (f[0], f[1..])).ToList()));
                continue;
            }

            // Ein Blocktag auf eigener Zeile nimmt seine Zeile mit, damit die Einrückung
            // der Vorlage nicht im Bericht landet.
            if (OwnLine(s, open))
            {
                Trim(text);
                if (i < s.Length && s[i] == '\n') i++;
            }

            var word = tag.Split(' ', 2)[0];
            if (Array.IndexOf(until, word) >= 0)
            {
                Flush(nodes, text);
                stopped = word;
                return nodes;
            }
            switch (word)
            {
                case "for":
                    {
                        Flush(nodes, text);
                        var parts = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length != 4 || parts[2] != "in") throw new TemplateError($"{{% {tag} %}}");
                        nodes.Add(new For(parts[1], parts[3], Parse(s, ref i, ["endfor"], out _)));
                        break;
                    }
                case "if":
                    {
                        Flush(nodes, text);
                        var parts = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var negated = parts.Length == 3 && parts[1] == "not";
                        var compared = parts.Length == 4 && parts[2] == "==" && parts[3].StartsWith('"') && parts[3].EndsWith('"');
                        if (!negated && !compared && parts.Length != 2) throw new TemplateError($"{{% {tag} %}}");
                        var then = Parse(s, ref i, ["else", "endif"], out var stop);
                        var other = stop == "else" ? Parse(s, ref i, ["endif"], out _) : [];
                        nodes.Add(new If(compared ? parts[1] : parts[^1], negated, compared ? parts[3][1..^1] : null, then, other));
                        break;
                    }
                default:
                    throw new TemplateError($"unbekannter Block {{% {tag} %}}");
            }
        }
        if (until.Length > 0) throw new TemplateError($"{{% {until[^1]} %}} fehlt");
        Flush(nodes, text);
        return nodes;
    }

    static string Esc(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var b = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            switch (ch)
            {
                case '&': b.Append("&amp;"); break;
                case '<': b.Append("&lt;"); break;
                case '>': b.Append("&gt;"); break;
                case '"': b.Append("&quot;"); break;
                case '\'': b.Append("&#39;"); break;
                default: b.Append(ch); break;
            }
        }
        return b.ToString();
    }

    sealed record Filter(string Usage, string Help, Func<JsonNode?, JsonNode?[], string> Apply);

    static readonly Dictionary<string, Filter> Filters = new()
    {
        ["date"] = new("date", "Datum JJJJ-MM-TT als TT.MM.JJJJ", (n, _) => Format.Date(DateOnly.Parse(Print(n)))),
        ["day"] = new("day", "Zeitpunkt als Tag TT.MM.JJJJ", (n, _) => Format.Day(DateTimeOffset.Parse(Print(n)))),
        ["sparte"] = new("sparte", "Sparte als Wort: Getränke, Speisen, Handelsware, Übrige", (n, _) => Print(n) switch { "getraenke" => "Getränke", "speisen" => "Speisen", "handelsware" => "Handelsware", _ => "Übrige" }),
        ["cents"] = new("cents", "Cent als Euro mit zwei Nachkommastellen", (n, _) => Format.Cents(Number(n))),
        ["bp"] = new("bp", "Basispunkte als Prozent", (n, _) => Format.Bp(Number(n))),
        ["portions"] = new("portions", "Anzahl mit „Portion“ oder „Portionen“", (n, _) => Format.Portions(Number(n))),
        ["group"] = new("group", "Ganzzahl mit Tausenderpunkten", (n, _) => Format.Group(Number(n))),
        ["milli"] = new("milli", "Tausendstel als Dezimalzahl", (n, _) => Format.Milli(Number(n))),
        ["micro"] = new("micro", "Millionstel als Dezimalzahl", (n, _) => Format.Micro(Number(n))),
        ["qty"] = new("qty:einheit", "Menge in der Rezepteinheit (g, ml, Stück), ab 1000 in kg oder l", (n, a) => Format.Qty(Number(n), Enum<Unit>(a[0]))),
        ["quantity"] = new("quantity:einheitencode", "Rechnungsmenge mit Einheitencode (UN/ECE Rec 20)", (n, a) => Format.Quantity(Number(n), Print(a[0]))),
        ["price"] = new("price:preisbasis:einheitencode", "Einzelpreis je Preisbasis und Einheit", (n, a) => Format.UnitPrice(Number(n), Number(a[0]), Print(a[1]))),
        ["unitname"] = new("unitname", "Einheitencode als Bezeichnung", (n, _) => Units.Label(Print(n))),
    };

    public static IEnumerable<(string Usage, string Help)> FilterHelp =>
        Filters.Values.OrderBy(f => f.Usage, StringComparer.Ordinal).Select(f => (f.Usage, f.Help));

    static T Enum<T>(JsonNode? n) where T : struct =>
        System.Enum.TryParse<T>(Print(n), true, out var v) ? v : throw new TemplateError($"{Print(n)} ist keine {typeof(T).Name}");

    static long Number(JsonNode? n) =>
        n is JsonValue v && v.TryGetValue<long>(out var l) ? l : throw new TemplateError($"{n?.GetPath()} ist keine Zahl");

    static void Flush(List<Node> nodes, StringBuilder text)
    {
        if (text.Length > 0) nodes.Add(new Text(text.ToString()));
        text.Clear();
    }

    static bool OwnLine(string s, int open)
    {
        for (var j = open - 1; j >= 0; j--)
        {
            if (s[j] == '\n') return true;
            if (s[j] != ' ' && s[j] != '\t') return false;
        }
        return true;
    }

    static void Trim(StringBuilder text)
    {
        var n = text.Length;
        while (n > 0 && (text[n - 1] == ' ' || text[n - 1] == '\t')) n--;
        text.Length = n;
    }

    static void Write(StringBuilder b, List<Node> nodes, JsonObject root, List<(string Name, JsonNode? Value)> scope)
    {
        foreach (var node in nodes)
            switch (node)
            {
                case Text t:
                    b.Append(t.Value);
                    break;
                case Value v:
                    var value = Resolve(v.Path, root, scope);
                    foreach (var (name, args) in v.Filters)
                        value = JsonValue.Create(Filters[name].Apply(value, [.. args.Select(a => Resolve(a, root, scope))]));
                    b.Append(Esc(Print(value)));
                    break;
                case For f:
                    var items = Resolve(f.Path, root, scope) switch
                    {
                        JsonArray a => a.AsEnumerable(),
                        JsonObject o => o.Select(kv => kv.Value),
                        _ => throw new TemplateError($"{f.Path} ist keine Liste"),
                    };
                    foreach (var item in items)
                    {
                        scope.Add((f.Name, item));
                        Write(b, f.Body, root, scope);
                        scope.RemoveAt(scope.Count - 1);
                    }
                    break;
                case If c:
                    var holds = c.Literal is { } literal
                        ? Print(Resolve(c.Path, root, scope)) == literal
                        : Truthy(Resolve(c.Path, root, scope));
                    Write(b, holds != c.Negated ? c.Then : c.Else, root, scope);
                    break;
            }
    }

    static JsonNode? Resolve(string path, JsonObject root, List<(string Name, JsonNode? Value)> scope)
    {
        JsonNode? node = null;
        var first = true;
        foreach (var (step, indexed) in Steps(path))
        {
            var key = !indexed || step.All(char.IsAsciiDigit) ? step : Print(Resolve(step, root, scope));
            if (first)
            {
                first = false;
                var found = false;
                for (var i = scope.Count - 1; i >= 0 && !found; i--)
                    if (scope[i].Name == key) (node, found) = (scope[i].Value, true);
                if (found) continue;
                if (!root.TryGetPropertyValue(key, out node)) throw new TemplateError($"unbekannt: {path}");
                continue;
            }
            node = node switch
            {
                JsonObject o when o.TryGetPropertyValue(key, out var value) => value,
                JsonArray a when int.TryParse(key, out var at) && at >= 0 && at < a.Count => a[at],
                _ => throw new TemplateError($"unbekannt: {path}"),
            };
        }
        return node;
    }

    static IEnumerable<(string Step, bool Indexed)> Steps(string path)
    {
        var at = 0;
        while (at < path.Length)
        {
            if (path[at] == '.') { at++; continue; }
            if (path[at] == '[')
            {
                var close = path.IndexOf(']', at);
                if (close < 0) throw new TemplateError($"unbekannt: {path}");
                yield return (path[(at + 1)..close], true);
                at = close + 1;
                continue;
            }
            var end = path.IndexOfAny(['.', '['], at);
            if (end < 0) end = path.Length;
            yield return (path[at..end], false);
            at = end;
        }
    }

    static bool Truthy(JsonNode? n) => n switch
    {
        null => false,
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        JsonValue v => v.TryGetValue<bool>(out var flag) ? flag
            : v.TryGetValue<string>(out var s) ? s.Length > 0
            : !v.TryGetValue<double>(out var d) || d != 0,
        _ => true,
    };

    static string Print(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v => v.TryGetValue<string>(out var s) ? s : v.ToJsonString(),
        _ => throw new TemplateError($"{n.GetPath()} ist kein Wert"),
    };
}

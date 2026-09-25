using System.Text.Json;
using System.Text.Json.Nodes;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

// A case in parts: each of its fields and lists, and each invoice on its own. A change keeps only the
// parts it touched, so setting it back leaves alone what another window changed meanwhile.
public static class CaseParts
{
    const string Invoice = "invoice:";

    static readonly HashSet<string> Whole = ["invoices", "createdAt", "updatedAt", "mappedAt"];

    public static Dictionary<string, string> Of(Case c)
    {
        var node = JsonSerializer.SerializeToNode(c, ModelJsonContext.Default.Case)!.AsObject();
        var parts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in node)
            if (!Whole.Contains(key) && value is not null) parts[key] = value.ToJsonString();
        foreach (var inv in node["invoices"]!.AsArray())
            parts[Invoice + (string)inv!["id"]!] = inv.ToJsonString();
        return parts;
    }

    // An invoice set to nothing leaves the case; one that comes back is added at the end.
    public static Case With(Case c, IReadOnlyDictionary<string, string?> parts)
    {
        var node = JsonSerializer.SerializeToNode(c, ModelJsonContext.Default.Case)!.AsObject();
        var invoices = node["invoices"]!.AsArray();
        foreach (var (key, json) in parts)
        {
            var value = json is null ? null : JsonNode.Parse(json);
            if (!key.StartsWith(Invoice, StringComparison.Ordinal))
            {
                if (value is null) node.Remove(key);
                else node[key] = value;
                continue;
            }
            var id = key[Invoice.Length..];
            var at = invoices.ToList().FindIndex(i => (string?)i?["id"] == id);
            if (at >= 0) invoices.RemoveAt(at);
            if (value is not null) invoices.Insert(at >= 0 ? at : invoices.Count, value);
        }
        return node.Deserialize(ModelJsonContext.Default.Case)!;
    }
}

public sealed class CaseChange(Session session, Dictionary<string, string?> before, Dictionary<string, string?> after) : IChange
{
    Dictionary<string, string?> Before { get; } = before;
    Dictionary<string, string?> After { get; } = after;

    public object Target => session;

    public static CaseChange? Between(Session session, Dictionary<string, string> was, Dictionary<string, string> now)
    {
        Dictionary<string, string?> before = new(StringComparer.Ordinal), after = new(StringComparer.Ordinal);
        foreach (var key in was.Keys.Union(now.Keys))
        {
            var (b, a) = (was.GetValueOrDefault(key), now.GetValueOrDefault(key));
            if (b == a) continue;
            before[key] = b;
            after[key] = a;
        }
        return before.Count == 0 ? null : new CaseChange(session, before, after);
    }

    public IChange Then(IChange later)
    {
        var next = (CaseChange)later;
        Dictionary<string, string?> b = new(Before), a = new(After);
        foreach (var (key, value) in next.After)
        {
            b.TryAdd(key, next.Before[key]);
            a[key] = value;
        }
        return new CaseChange(session, b, a);
    }

    public string? Stale(bool back)
    {
        if (session.Case is not { } kase) return History.Overtaken(back);
        var now = CaseParts.Of(kase);
        return (back ? After : Before).All(p => now.GetValueOrDefault(p.Key) == p.Value) ? null : History.Overtaken(back);
    }

    public Task<bool> Apply(bool back) =>
        session.Case is { } kase ? session.Restore(CaseParts.With(kase, back ? Before : After)) : Task.FromResult(false);
}

public sealed class RuleChange(Session session, Entity kind, string id, IRuleEntity? before, IRuleEntity? after) : IChange
{
    IRuleEntity? After { get; } = after;

    public object Target => (kind, id);

    public static Entity KindOf(IRuleEntity e) => e switch
    {
        Category => Entity.Category,
        Ingredient => Entity.Ingredient,
        ArticleMapping => Entity.Mapping,
        Product => Entity.Product,
        YieldRule => Entity.YieldRule,
        Gewerbezweig => Entity.Gewerbezweig,
        ReportTemplate => Entity.Template,
        _ => throw new ArgumentException("unbekannte Regel " + e.GetType().Name),
    };

    public IChange Then(IChange later) => new RuleChange(session, kind, id, before, ((RuleChange)later).After);

    public string? Stale(bool back) =>
        Content(session.Rules?.Find(kind, id)) == Content(back ? After : before) ? null : History.Overtaken(back);

    public Task<bool> Apply(bool back) => session.Restore(kind, id, back ? before : After);

    // What a rule says, apart from when and in which revision it was written.
    static string? Content(IRuleEntity? e)
    {
        if (e is null) return null;
        var bare = Json.Copy(e);
        bare.Meta = new Meta();
        return JsonSerializer.Serialize(bare, bare.GetType(), ModelJsonContext.Default);
    }
}

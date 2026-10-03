using System.Text;
using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Model;

public enum Entity
{
    [JsonStringEnumMemberName("category")] Category,
    [JsonStringEnumMemberName("mapping")] Mapping,
    [JsonStringEnumMemberName("product")] Product,
    [JsonStringEnumMemberName("yield_rule")] YieldRule,
    [JsonStringEnumMemberName("gewerbe")] Gewerbezweig,
    [JsonStringEnumMemberName("template")] Template,
}

// Die Sparte trennt den Rohgewinnaufschlag einer Gaststätte, wie ihn die Prüfung erwartet:
// Getränke tragen einen anderen Satz als Speisen, Handelsware wie Tabak einen dritten.
public enum Sparte
{
    [JsonStringEnumMemberName("unbestimmt")] Unbestimmt,
    [JsonStringEnumMemberName("getraenke")] Getränke,
    [JsonStringEnumMemberName("speisen")] Speisen,
    [JsonStringEnumMemberName("handelsware")] Handelsware,
}

public static class Clock
{
    public static DateTimeOffset Now() => DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
}

public sealed class Meta
{
    public DateOnly? ValidFrom { get; set; }
    public DateOnly? ValidTo { get; set; }
    public DateTimeOffset ChangedAt { get; set; }
    public string? ChangedBy { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long Rev { get; set; }

    public bool ValidOn(DateOnly? d)
    {
        if (d is null) return true;
        if (ValidFrom is { } from && d < from) return false;
        return ValidTo is null || d < ValidTo;
    }
}

public interface IRuleEntity
{
    string Id { get; set; }
    Meta Meta { get; set; }
}

public sealed class Category : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    // Gewerbekennzahlen der Richtsatzsammlung, auch als Präfix ("561" für alle Gastronomie).
    // Leer heißt: in jedem Gewerbe.
    public List<string> Gewerbe { get; set; } = [];
    // Packmittel, in denen die Ware geliefert wird ("XKG" für Bier vom Fass). Leer heißt: in jedem.
    public List<string> Gebinde { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Sparte Sparte { get; set; }
    public Meta Meta { get; set; } = new();

    public bool Covers(string? kennzahl) =>
        string.IsNullOrEmpty(kennzahl) || Gewerbe.Count == 0 || Gewerbe.Exists(kennzahl.StartsWith);

    public bool Contradicts(IReadOnlySet<string> containers) =>
        Gebinde.Count > 0 && containers.Count > 0 && !Gebinde.Exists(containers.Contains);
}

// Eine Gewerbekennzahl, die eine Prüfung wählen kann. Kategorien grenzen ihre Produkte per
// Präfix auf Kennzahlen ein; eine Kennzahl, die keine kennt, ließe der Zuordnung nichts übrig.
public sealed class Gewerbezweig : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Kennzahl { get; set; } = "";
    public string Name { get; set; } = "";
    public Meta Meta { get; set; } = new();
}

public sealed record Piece(long Amount, Unit Unit);

public sealed class ArticleMapping : IRuleEntity
{
    public string Id { get; set; } = "";
    public string? SupplierName { get; set; }
    public string? SupplierArticleId { get; set; }
    public string? Gtin { get; set; }
    public string? Name { get; set; }
    // The invoice wording this mapping was made from. Never matched against — it is
    // what the suggester learns a supplier's vocabulary from.
    public string? Observed { get; set; }
    public string? UnitCode { get; set; }
    public string ProductId { get; set; } = "";
    // Inhalt eines Gebindes in der Einheit des Produkts; null, wo die Einheitentabelle schon umrechnet.
    public long? Factor { get; set; }
    public bool Confirmed { get; set; }
    public Meta Meta { get; set; } = new();
}

public static class ArticleName
{
    public static string Canonical(string? s)
    {
        var b = new StringBuilder();
        var gap = false;
        foreach (var c in (s ?? "").ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == ',')
            {
                if (gap && b.Length > 0) b.Append(' ');
                gap = false;
                b.Append(c);
            }
            else gap = true;
        }
        return b.ToString();
    }
}

public enum OriginKind { Exact, Encoder, Manual }

public static class Match
{
    public static ArticleMapping? Mapping(RuleSet rs, string? supplier, DateOnly? date, InvoiceLine line)
    {
        if (!string.IsNullOrEmpty(line.MappingId) && rs.Mappings.TryGetValue(line.MappingId, out var direct))
            return direct;

        var ids = new List<string>(rs.Mappings.Keys);
        ids.Sort(StringComparer.Ordinal);
        var candidates = new List<ArticleMapping>(ids.Count);
        foreach (var id in ids)
            if (Usable(rs.Mappings[id], date, line)) candidates.Add(rs.Mappings[id]);

        foreach (var m in candidates)
            if (ByArticle(m, supplier, line)) return m;
        foreach (var m in candidates)
            if (ByGtin(m, line)) return m;
        foreach (var m in candidates)
            if (ByName(m, line)) return m;
        return null;
    }

    // Whether a line still belongs to the mapping it carries: an edited article number
    // or unit leaves the rule behind.
    public static bool Fits(ArticleMapping m, string? supplier, DateOnly? date, InvoiceLine line) =>
        Usable(m, date, line) && (ByArticle(m, supplier, line) || ByGtin(m, line) || ByName(m, line));

    static bool Usable(ArticleMapping m, DateOnly? date, InvoiceLine line) =>
        m.Meta.ValidOn(date)
        && (string.IsNullOrEmpty(m.UnitCode) || string.IsNullOrEmpty(line.UnitCode)
            || string.Equals(m.UnitCode, line.UnitCode, StringComparison.OrdinalIgnoreCase));

    static bool ByArticle(ArticleMapping m, string? supplier, InvoiceLine line) =>
        !string.IsNullOrEmpty(supplier) && !string.IsNullOrEmpty(line.SellerArticleId)
        && m.SupplierName == supplier && m.SupplierArticleId == line.SellerArticleId;

    static bool ByGtin(ArticleMapping m, InvoiceLine line) =>
        !string.IsNullOrEmpty(line.Gtin) && m.Gtin == line.Gtin;

    static bool ByName(ArticleMapping m, InvoiceLine line)
    {
        var name = ArticleName.Canonical(line.Name);
        return name != "" && ArticleName.Canonical(m.Name) == name;
    }

    // The Prüfung's choice first, the product's before its category's; a choice without a rule is "no deduction".
    // Without a choice the default of the product, then of its category.
    public static YieldRule? YieldRule(Case c, RuleSet rs, Product p)
    {
        var category = string.IsNullOrEmpty(p.CategoryId) ? null : p.CategoryId;
        YieldChoice? byProduct = null, byCategory = null;
        foreach (var y in c.Yields)
        {
            if (y.ProductId == p.Id) byProduct = y;
            else if (string.IsNullOrEmpty(y.ProductId) && category is not null && y.CategoryId == category)
                byCategory = y;
        }
        foreach (var chosen in new[] { byProduct, byCategory })
        {
            if (chosen is null) continue;
            if (chosen.YieldRuleId is not { } id) return null;
            if (rs.YieldRules.TryGetValue(id, out var r)) return r;
        }
        return rs.YieldRules.Values.FirstOrDefault(r => r.Default && r.ProductId == p.Id)
            ?? (category is null ? null
                : rs.YieldRules.Values.FirstOrDefault(r => r.Default && string.IsNullOrEmpty(r.ProductId) && r.CategoryId == category));
    }
}

// Wie viel eines anderen Produkts ein Rezept braucht, in einer Einheit, die sich in dessen umrechnen lässt.
public sealed class PartLine
{
    public string PartId { get; set; } = "";
    public long Amount { get; set; }
    public string Unit { get; set; } = "";
}

// Ein Produkt ist, was gekauft, gelagert, hergestellt oder verkauft wird, und jedes kann alles davon:
// „Schnitzel mit Pommes“ besteht aus einem Schnitzel und einer Portion Pommes, die Pommes aus
// Kartoffeln. Gezählt wird es in seiner Einheit; ein Rezept gilt für Batch davon.
public sealed class Product : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Unit { get; set; } = "";
    public long Batch { get; set; } = 1;
    public string? CategoryId { get; set; }
    // Warenarten, die unter diesem Produkt gebucht werden: "Gouda" bei Schnittkäse. Der
    // Zuordner sucht in Name und Aliassen; sie sind, was ein Prüfer statt Namensmustern pflegt.
    public List<string> Aliases { get; set; } = [];
    // Richtwert für ein Stück in g oder ml: macht "1 Stk Gurke" ohne Faktor zu 400 g.
    public Piece? Piece { get; set; }
    public List<PartLine> Recipe { get; set; } = [];
    public Meta Meta { get; set; } = new();
}

public sealed class YieldRule : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CategoryId { get; set; }
    public string? ProductId { get; set; }
    public long Deduction { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Default { get; set; }
    public Meta Meta { get; set; } = new();
}

// Die Vorlage, aus der ein Bericht entsteht. Eine ist der Standard; eine Prüfung kann eine andere wählen.
public sealed class ReportTemplate : IRuleEntity
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Source { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Default { get; set; }
    public Meta Meta { get; set; } = new();
}

// A set is read, never changed: the store keeps one and hands the same to everyone who asks, the
// interface included. What is to be changed is changed on a copy of the entity and put through the store.
public sealed class RuleSet
{
    // Kennung der Regel-Datenbank; Version zählt nur innerhalb ihrer.
    public string? Store { get; set; }
    public long Version { get; set; }
    public Dictionary<string, Category> Categories { get; set; } = [];
    public Dictionary<string, ArticleMapping> Mappings { get; set; } = [];
    public Dictionary<string, Product> Products { get; set; } = [];
    public Dictionary<string, YieldRule> YieldRules { get; set; } = [];
    public Dictionary<string, Gewerbezweig> Gewerbezweige { get; set; } = [];
    public Dictionary<string, ReportTemplate> Templates { get; set; } = [];

    // Die Zuordnungen einer Prüfung ergänzen die der Regeln; unter derselben ID gilt die Regel,
    // und eine, deren Produkt es hier nicht gibt, fällt weg.
    public RuleSet With(IReadOnlyDictionary<string, ArticleMapping>? own)
    {
        if (own is null) return this;
        Dictionary<string, ArticleMapping>? mappings = null;
        foreach (var (id, m) in own)
        {
            if (Mappings.ContainsKey(id) || !Products.ContainsKey(m.ProductId)) continue;
            mappings ??= new Dictionary<string, ArticleMapping>(Mappings);
            mappings[id] = m;
        }
        if (mappings is null) return this;
        return new RuleSet
        {
            Store = Store,
            Version = Version,
            Categories = Categories,
            Mappings = mappings,
            Products = Products,
            YieldRules = YieldRules,
            Gewerbezweige = Gewerbezweige,
            Templates = Templates,
        };
    }

    public ReportTemplate? Template(string? id) =>
        (id is not null ? Templates.GetValueOrDefault(id) : null) ?? Templates.Values.FirstOrDefault(t => t.Default);

    public IRuleEntity? Find(Entity entity, string id) => entity switch
    {
        Entity.Category => Categories.GetValueOrDefault(id),
        Entity.Mapping => Mappings.GetValueOrDefault(id),
        Entity.Product => Products.GetValueOrDefault(id),
        Entity.YieldRule => YieldRules.GetValueOrDefault(id),
        Entity.Gewerbezweig => Gewerbezweige.GetValueOrDefault(id),
        Entity.Template => Templates.GetValueOrDefault(id),
        _ => throw new ArgumentException("unbekannte Regelart " + entity),
    };

    public IEnumerable<IRuleEntity> Entries() =>
        new IEnumerable<IRuleEntity>[] { Categories.Values, Products.Values, Mappings.Values, YieldRules.Values, Gewerbezweige.Values, Templates.Values }
            .SelectMany(e => e);

    // What turns this set back into `then`: each entry of it that reads otherwise here, and null for
    // each one here that it did not have. Apart from when it was written, an entry is what it says.
    public List<(Entity Kind, string Id, IRuleEntity? Rule)> Back(RuleSet then)
    {
        List<(Entity, string, IRuleEntity?)> steps = [];
        foreach (var e in then.Entries())
            if (Content(Find(KindOf(e), e.Id)) != Content(e)) steps.Add((KindOf(e), e.Id, e));
        foreach (var e in Entries())
            if (then.Find(KindOf(e), e.Id) is null) steps.Add((KindOf(e), e.Id, null));
        return steps;
    }

    public static string? Content(IRuleEntity? e)
    {
        if (e is null) return null;
        var bare = Json.Copy(e);
        bare.Meta = new Meta { ValidFrom = e.Meta.ValidFrom, ValidTo = e.Meta.ValidTo };
        return System.Text.Json.JsonSerializer.Serialize(bare, bare.GetType(), ModelJsonContext.Default);
    }

    public static Entity KindOf(IRuleEntity e) => e switch
    {
        Category => Entity.Category,
        ArticleMapping => Entity.Mapping,
        Product => Entity.Product,
        YieldRule => Entity.YieldRule,
        Gewerbezweig => Entity.Gewerbezweig,
        ReportTemplate => Entity.Template,
        _ => throw new ArgumentException("unbekannte Regel " + e.GetType().Name),
    };

    public bool Remove(Entity kind, string id) => kind switch
    {
        Entity.Category => Categories.Remove(id),
        Entity.Mapping => Mappings.Remove(id),
        Entity.Product => Products.Remove(id),
        Entity.YieldRule => YieldRules.Remove(id),
        Entity.Gewerbezweig => Gewerbezweige.Remove(id),
        Entity.Template => Templates.Remove(id),
        _ => throw new ArgumentException("unbekannte Regelart " + kind),
    };

    public void Put(IRuleEntity e)
    {
        switch (e)
        {
            case Category x: Categories[x.Id] = x; break;
            case ArticleMapping x: Mappings[x.Id] = x; break;
            case Product x: Products[x.Id] = x; break;
            case YieldRule x: YieldRules[x.Id] = x; break;
            case Gewerbezweig x: Gewerbezweige[x.Id] = x; break;
            case ReportTemplate x: Templates[x.Id] = x; break;
        }
    }
}

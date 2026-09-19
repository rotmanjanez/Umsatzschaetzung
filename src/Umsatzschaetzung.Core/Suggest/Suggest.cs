using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

public sealed record Suggestion(ArticleMapping Mapping, int Confidence, OriginKind Kind);

public sealed class Matcher
{
    const int Candidates = 5;
    const double MinScore = 0.4;

    // Load() hands out a fresh RuleSet every call, so the version is the key: one
    // Matcher belongs to one rule store and reindexes only once a save bumps it or a
    // case from another Gewerbe asks.
    long indexed = -1;
    string indexedGewerbe = "";
    Lexicon? lexicon;
    Evidence? evidence;

    public List<Suggestion> Suggest(RuleSet rs, string gewerbe, string? supplier, InvoiceLine line)
    {
        if (Match.Mapping(rs, supplier, DateOnly.FromDateTime(DateTime.Now), line) is { } hit)
            return [new Suggestion(hit, 100, OriginKind.Exact)];

        Index(rs, gewerbe);
        var scores = lexicon!.Score(line.Name);
        foreach (var (id, learned) in evidence!.Score(line.Name, supplier))
            scores[id] = Fuse(scores.GetValueOrDefault(id), learned);

        var pack = PackSize.Read(line.Name);
        return scores
            .Where(s => s.Value >= MinScore && rs.Ingredients.ContainsKey(s.Key))
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => (Score: s.Value, Ingredient: rs.Ingredients[s.Key]))
            .Take(Candidates)
            .Select(c => new Suggestion(
                Mapping(supplier, line, c.Ingredient.Id, Factor(pack, line.UnitCode, Scale.Of(rs, c.Ingredient.Id))),
                Confidence(c.Score),
                OriginKind.Lexical))
            .ToList();
    }

    // Either signal on its own can carry a candidate, and two weak ones that agree
    // beat one. Nothing to weight by hand: with no history the evidence term is 0
    // and the name match decides, as it did before there was anything to learn from.
    static double Fuse(double name, double learned) => 1 - (1 - name) * (1 - learned);

    // Only the ingredients of the case's Gewerbe are candidates: a Gaststätte is never
    // offered Blondierpulver, and the words that tell its own goods apart weigh more.
    void Index(RuleSet rs, string gewerbe)
    {
        if (indexed == rs.Version && indexedGewerbe == gewerbe && lexicon is not null && evidence is not null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var active = rs.Ingredients.Values
            .Where(i => i.Meta.ValidOn(today) && (!rs.Categories.TryGetValue(i.CategoryId, out var c) || c.Covers(gewerbe)))
            .OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        var ids = active.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var log = rs.Mappings.Keys.Order(StringComparer.Ordinal)
            .Select(id => rs.Mappings[id])
            .Where(m => m.Meta.ValidOn(today))
            .ToList();
        lexicon = new Lexicon(Names(active, ids, log));
        evidence = new Evidence(log, ids);
        indexed = rs.Version;
        indexedGewerbe = gewerbe;
    }

    // Character matching sees the ingredient's own name and the wording of mappings
    // somebody checked. Unreviewed wording is left to Evidence, which discounts it.
    static IEnumerable<(Ingredient, string)> Names(List<Ingredient> active, HashSet<string> ids, List<ArticleMapping> log)
    {
        var byId = active.ToDictionary(i => i.Id, StringComparer.Ordinal);
        foreach (var ing in active) yield return (ing, ing.Name);
        foreach (var m in log)
            if (m.Confirmed && Wording(m) is { } text && ids.Contains(m.IngredientId))
                yield return (byId[m.IngredientId], text);
    }

    public static string? Wording(ArticleMapping m) =>
        !string.IsNullOrEmpty(m.Observed) ? m.Observed : !string.IsNullOrEmpty(m.Name) ? m.Name : null;

    static int Confidence(double score) => Math.Clamp((int)Math.Round(score * 100), 1, 99);

    static ArticleMapping Mapping(string? supplier, InvoiceLine line, string ingredientId, long? factor)
    {
        var m = new ArticleMapping
        {
            SupplierName = supplier,
            Gtin = line.Gtin,
            Observed = line.Name,
            IngredientId = ingredientId,
            Factor = factor,
        };
        if (!string.IsNullOrEmpty(supplier)) m.SupplierArticleId = line.SellerArticleId;
        if (string.IsNullOrEmpty(m.SupplierArticleId) && string.IsNullOrEmpty(m.Gtin)) m.Name = line.Name;
        return m;
    }

    // Der Inhalt eines Gebindes in der Rezepteinheit, aus der Packungsangabe in der
    // Bezeichnung. Null heißt: entweder rechnet die Einheitentabelle ohnehin um, oder
    // die Zeile braucht einen Faktor, den nur ein Mensch kennt.
    public static long? Factor(Pack? pack, string unitCode, Unit? recipeUnit)
    {
        if (recipeUnit is not { } unit) return null;
        if (Units.Lookup(unitCode) is { Container: false } u && u.Base == unit) return null;
        if (pack is not { } p) return null;
        if (unit == Unit.Piece) return p.Base == Unit.Piece ? p.Count : null;
        return p.Base == unit || p.Base is null ? p.Count * p.Size : null;
    }
}

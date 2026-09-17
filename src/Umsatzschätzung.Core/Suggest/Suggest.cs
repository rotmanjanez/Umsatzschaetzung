using Umsatzschätzung.Model;

namespace Umsatzschätzung.Suggest;

public sealed record Suggestion(ArticleMapping Mapping, int Confidence, OriginKind Kind);

public sealed class Matcher
{
    const int Candidates = 5;
    const double MinScore = 0.4;

    // Load() hands out a fresh RuleSet every call, so the version is the key: one
    // Matcher belongs to one rule store and reindexes only once a save bumps it.
    long indexed = -1;
    Lexicon? lexicon;
    Evidence? evidence;

    public List<Suggestion> Suggest(RuleSet rs, string? supplier, InvoiceLine line)
    {
        if (Match.Mapping(rs, supplier, DateOnly.FromDateTime(DateTime.Now), line) is { } hit)
            return [new Suggestion(hit, 100, OriginKind.Exact)];

        Index(rs);
        var scores = lexicon!.Score(line.Name);
        foreach (var (id, learned) in evidence!.Score(line.Name, supplier))
            scores[id] = Fuse(scores.GetValueOrDefault(id), learned);

        var pack = PackSize.Read(line.Name);
        return scores
            .Where(s => s.Value >= MinScore && rs.Ingredients.ContainsKey(s.Key))
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => (Score: s.Value, Ingredient: rs.Ingredients[s.Key]))
            .Select(c => (c.Score, c.Ingredient, Factor: Factor(pack, line.UnitCode, c.Ingredient.BaseUnit)))
            .Where(c => c.Factor is not null)
            .Take(Candidates)
            .Select(c => new Suggestion(
                Mapping(supplier, line, c.Ingredient.Id, c.Factor!.Value),
                Confidence(c.Score),
                OriginKind.Lexical))
            .ToList();
    }

    // Either signal on its own can carry a candidate, and two weak ones that agree
    // beat one. Nothing to weight by hand: with no history the evidence term is 0
    // and the name match decides, as it did before there was anything to learn from.
    static double Fuse(double name, double learned) => 1 - (1 - name) * (1 - learned);

    void Index(RuleSet rs)
    {
        if (indexed == rs.Version && lexicon is not null && evidence is not null) return;
        var today = DateOnly.FromDateTime(DateTime.Now);
        var active = rs.Ingredients.Values.Where(i => i.Meta.ValidOn(today))
            .OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        var ids = active.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var log = rs.Mappings.Keys.Order(StringComparer.Ordinal)
            .Select(id => rs.Mappings[id])
            .Where(m => m.Meta.ValidOn(today))
            .ToList();
        lexicon = new Lexicon(Names(active, ids, log));
        evidence = new Evidence(log, ids);
        indexed = rs.Version;
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

    static ArticleMapping Mapping(string? supplier, InvoiceLine line, string ingredientId, long factor)
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

    // One invoice unit in base units. From the pack size in the description, or
    // else from the billed unit when that alone already fixes the amount.
    public static long? Factor(Pack? pack, string unitCode, Unit baseUnit)
    {
        if (Packed(pack, baseUnit) is { } packed) return packed;
        if (Units.Lookup(unitCode) is not { } u || u.Base != baseUnit) return null;
        return u.Factor;
    }

    // A pack size that does not fit the base unit says nothing about the amount —
    // "Brötchen Weizen 55 g" billed per piece is a weight per piece, not a pack.
    // The billed unit still answers it, so a mismatch here falls through rather
    // than dropping the candidate.
    static long? Packed(Pack? pack, Unit baseUnit)
    {
        if (pack is not { } p) return null;
        if (baseUnit == Unit.Piece) return p.Base == Unit.Piece ? p.Count : null;
        return p.Base == baseUnit || p.Base is null ? p.Count * p.Size : null;
    }
}

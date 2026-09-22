using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

public sealed record Suggestion(ArticleMapping Mapping, int Confidence, OriginKind Kind);

// Embedding a text costs a model run. The ingredient list changes rarely and suppliers
// repeat their wordings, so what was embedded once is kept.
public interface IEmbeddingCache
{
    Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts);
    void Write(string model, IReadOnlyList<(string Text, float[] Vec)> rows);
}

public sealed class Matcher(IEmbeddingCache? cache = null) : IDisposable
{
    const int Candidates = 5;
    const int Floor = 20;

    readonly Encoder encoder = new();
    readonly Lock gate = new();

    // Load() hands out a fresh RuleSet every call, so the version is the key: one
    // Matcher belongs to one rule store and reindexes only once a save bumps it or a
    // case from another Gewerbe asks. Validity is a question of the invoice's date and
    // is asked per query, not baked into the index.
    long indexed = -1;
    string indexedGewerbe = "";
    string[] owner = [];
    Meta[] ware = [];
    Meta?[] rule = [];
    float[] vectors = [];

    public void Dispose() => encoder.Dispose();

    // An exact hit leads, and the encoder's alternatives follow it: revising a mapped
    // line needs them as much as an open line does.
    public List<Suggestion> Suggest(RuleSet rs, string gewerbe, string? supplier, InvoiceLine line, DateOnly? date = null)
    {
        lock (gate) return Rank(rs, gewerbe, supplier, line, date ?? DateOnly.FromDateTime(DateTime.Now));
    }

    List<Suggestion> Rank(RuleSet rs, string gewerbe, string? supplier, InvoiceLine line, DateOnly date)
    {
        var hit = Match.Mapping(rs, supplier, date, line);
        Index(rs, gewerbe);
        var query = Embed([Normal(line.Name)])[0];
        var best = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < owner.Length; i++)
        {
            if (!ware[i].ValidOn(date) || rule[i]?.ValidOn(date) == false) continue;
            var cos = Dot(query, i);
            if (!best.TryGetValue(owner[i], out var b) || cos > b) best[owner[i]] = cos;
        }

        var pack = PackSize.Read(line.Name);
        var sugs = best
            .Where(s => s.Key != hit?.IngredientId)
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Take(Candidates)
            .Select(s => (Confidence: encoder.Confidence(s.Value), Ingredient: rs.Ingredients[s.Key]))
            .Where(c => c.Confidence >= Floor)
            .Select(c => new Suggestion(
                Mapping(supplier, line, c.Ingredient.Id, Factor(pack, line.UnitCode, Scale.Of(rs, c.Ingredient.Id))),
                c.Confidence,
                OriginKind.Encoder))
            .ToList();
        if (hit is not null) sugs.Insert(0, new Suggestion(hit, 100, OriginKind.Exact));
        return sugs;
    }

    double Dot(float[] query, int entry)
    {
        var at = entry * Encoder.Width;
        var sum = 0.0;
        for (var i = 0; i < Encoder.Width; i++) sum += query[i] * vectors[at + i];
        return sum;
    }

    // Only the ingredients of the case's Gewerbe are candidates: a Gaststätte is never
    // offered Blondierpulver. Every name a ware is known under is its own entry — the
    // ingredient is whichever of its wordings comes closest, not their average.
    void Index(RuleSet rs, string gewerbe)
    {
        if (indexed == rs.Version && indexedGewerbe == gewerbe) return;
        var covered = rs.Ingredients.Values
            .Where(i => !rs.Categories.TryGetValue(i.CategoryId, out var c) || c.Covers(gewerbe))
            .OrderBy(i => i.Id, StringComparer.Ordinal)
            .ToDictionary(i => i.Id, StringComparer.Ordinal);

        var texts = new List<string>();
        var owners = new List<string>();
        var wares = new List<Meta>();
        var rules = new List<Meta?>();
        var seen = new HashSet<(string, string, Meta?)>();
        void Add(Ingredient ing, Meta? by, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = Normal(text);
            if (by is not null && seen.Contains((ing.Id, text, null))) return;
            if (!seen.Add((ing.Id, text, by))) return;
            owners.Add(ing.Id);
            wares.Add(ing.Meta);
            rules.Add(by);
            texts.Add(text);
        }

        foreach (var ing in covered.Values)
        {
            Add(ing, null, ing.Name);
            foreach (var alias in ing.Aliases) Add(ing, null, alias);
        }
        // A confirmed mapping is a wording a human tied to a ware; the next line that
        // reads like it lands on the same ware without asking again.
        foreach (var id in rs.Mappings.Keys.Order(StringComparer.Ordinal))
        {
            var m = rs.Mappings[id];
            if (m.Confirmed && covered.TryGetValue(m.IngredientId, out var ing)) Add(ing, m.Meta, Wording(m));
        }

        var embedded = Embed(texts);
        vectors = new float[texts.Count * Encoder.Width];
        for (var i = 0; i < embedded.Length; i++) embedded[i].CopyTo(vectors, i * Encoder.Width);
        owner = [.. owners];
        ware = [.. wares];
        rule = [.. rules];
        indexed = rs.Version;
        indexedGewerbe = gewerbe;
    }

    float[][] Embed(IReadOnlyList<string> texts)
    {
        var want = texts.Distinct(StringComparer.Ordinal).ToList();
        var known = cache?.Read(Encoder.Name, want) ?? [];
        var missing = want.FindAll(t => !known.ContainsKey(t));
        if (missing.Count > 0)
        {
            var fresh = encoder.Embed(missing);
            for (var i = 0; i < missing.Count; i++) known[missing[i]] = fresh[i];
            cache?.Write(Encoder.Name, [.. missing.Select((t, i) => (t, fresh[i]))]);
        }
        return [.. texts.Select(t => known[t])];
    }

    // Invoices shout, catalogues do not, and the encoder learnt from catalogues: a
    // wording without a lower-case letter is read as if it were written in title case.
    // Measured on 600 labelled rows, upper case keeps 9 % of the auto-mappings, title
    // case 39 % of the 41 % the original spelling gets.
    public static string Normal(string text)
    {
        if (text.Any(char.IsLower)) return text;
        var b = new System.Text.StringBuilder(text.Length);
        var start = true;
        foreach (var c in text)
        {
            b.Append(start ? c : char.ToLowerInvariant(c));
            start = !char.IsLetter(c);
        }
        return b.ToString();
    }

    public static string? Wording(ArticleMapping m) =>
        !string.IsNullOrEmpty(m.Observed) ? m.Observed : !string.IsNullOrEmpty(m.Name) ? m.Name : null;

    static ArticleMapping Mapping(string? supplier, InvoiceLine line, string ingredientId, long? factor)
    {
        var m = new ArticleMapping
        {
            SupplierName = supplier,
            Gtin = line.Gtin,
            Observed = line.Name,
            UnitCode = string.IsNullOrEmpty(line.UnitCode) ? null : line.UnitCode,
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

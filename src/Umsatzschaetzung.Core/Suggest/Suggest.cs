using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

public sealed record Suggestion(ArticleMapping Mapping, int Confidence, OriginKind Kind);

// Embedding the whole catalog costs a model run per wording, so the index's vectors are kept.
// Only the index's: an invoice line is looked up but never written, so nothing of a case reaches the shared cache.
public interface IEmbeddingCache
{
    Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts);
    void Write(string model, IReadOnlyList<(string Text, float[] Vec)> rows);
}

// Turns article wordings into vectors whose dot product says how alike they read.
public interface IEncoder
{
    const int Width = 768;

    string Model { get; }
    float[][] Embed(IReadOnlyList<string> texts);
    int Confidence(double cos);
}

// Without an encoder only an exact hit is known.
public sealed class Matcher(IEncoder? encoder, IEmbeddingCache? cache = null)
{
    const int Candidates = 5;
    const int Floor = 20;
    const double Mismatch = 0.1;

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
    string[] wording = [];
    float[] vectors = [];

    // An exact hit leads, and the encoder's alternatives follow it: revising a mapped
    // line needs them as much as an open line does.
    public List<Suggestion> Suggest(RuleSet rs, string gewerbe, string? supplier, InvoiceLine line, DateOnly? date = null)
    {
        lock (gate) return Rank(rs, gewerbe, supplier, line, date ?? DateOnly.FromDateTime(DateTime.Now));
    }

    List<Suggestion> Rank(RuleSet rs, string gewerbe, string? supplier, InvoiceLine line, DateOnly date)
    {
        var hit = Match.Mapping(rs, supplier, date, line);
        if (encoder is not { } e) return hit is null ? [] : [new Suggestion(hit, 100, OriginKind.Exact)];
        Index(e, rs, gewerbe);
        var query = Embed(e, [Normal(line.Name)], keep: false)[0];
        var best = new Dictionary<string, double>(StringComparer.Ordinal);
        for (var i = 0; i < owner.Length; i++)
        {
            if (!ware[i].ValidOn(date) || rule[i]?.ValidOn(date) == false) continue;
            var cos = Dot(query, i);
            if (!best.TryGetValue(owner[i], out var b) || cos > b) best[owner[i]] = cos;
        }

        // The encoder reads "Pils" and hardly the keg it comes in; the packaging decides
        // between wares the words cannot tell apart.
        var held = Containers(line);
        foreach (var id in best.Keys)
            if (rs.Categories.GetValueOrDefault(rs.Ingredients[id].CategoryId)?.Contradicts(held) == true)
                best[id] -= Mismatch;

        var pack = PackSize.Read(line.Name);
        var sugs = best
            .Where(s => s.Key != hit?.IngredientId)
            .OrderByDescending(s => s.Value)
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .Take(Candidates)
            .Select(s => (Confidence: e.Confidence(s.Value), Ingredient: rs.Ingredients[s.Key]))
            .Where(c => c.Confidence >= Floor)
            .Select(c => new Suggestion(
                Mapping(supplier, line, c.Ingredient.Id, Packed(rs, c.Ingredient, line.UnitCode, pack)),
                c.Confidence,
                OriginKind.Encoder))
            .ToList();
        if (hit is not null) sugs.Insert(0, new Suggestion(hit, 100, OriginKind.Exact));
        return sugs;
    }

    // The line's own unit and every container its wording names: "Pils Fass 30 l KEG" is a keg
    // whatever the supplier booked it in.
    static HashSet<string> Containers(InvoiceLine line)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in line.Name.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Append(line.UnitCode))
            if (Units.Lookup(word) is { Container: true } u) held.Add(u.Code);
        return held;
    }

    static readonly char[] Separators = [' ', ',', ';', '/', '(', ')'];

    double Dot(float[] query, int entry)
    {
        var at = entry * IEncoder.Width;
        var sum = 0.0;
        for (var i = 0; i < IEncoder.Width; i++) sum += query[i] * vectors[at + i];
        return sum;
    }

    // Only the ingredients of the case's Gewerbe are candidates: a Gaststätte is never
    // offered Blondierpulver. Every name a ware is known under is its own entry — the
    // ingredient is whichever of its wordings comes closest, not their average.
    void Index(IEncoder e, RuleSet rs, string gewerbe)
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

        // A save bumps the version for every mapping the import proposes, and almost none
        // of them adds a wording: the vectors stay as they are, or what the last index holds
        // is carried over, not read again.
        if (!texts.SequenceEqual(wording, StringComparer.Ordinal))
        {
            var prior = new Dictionary<string, int>(wording.Length, StringComparer.Ordinal);
            for (var i = 0; i < wording.Length; i++) prior.TryAdd(wording[i], i);
            var fresh = texts.Where(t => !prior.ContainsKey(t)).Distinct(StringComparer.Ordinal).ToList();
            var embedded = fresh.Zip(Embed(e, fresh, keep: true)).ToDictionary(StringComparer.Ordinal);
            var next = new float[texts.Count * IEncoder.Width];
            for (var i = 0; i < texts.Count; i++)
            {
                var into = next.AsSpan(i * IEncoder.Width, IEncoder.Width);
                if (prior.TryGetValue(texts[i], out var at)) vectors.AsSpan(at * IEncoder.Width, IEncoder.Width).CopyTo(into);
                else embedded[texts[i]].CopyTo(into);
            }
            vectors = next;
            wording = [.. texts];
        }
        owner = [.. owners];
        ware = [.. wares];
        rule = [.. rules];
        indexed = rs.Version;
        indexedGewerbe = gewerbe;
    }

    float[][] Embed(IEncoder e, IReadOnlyList<string> texts, bool keep)
    {
        var want = texts.Distinct(StringComparer.Ordinal).ToList();
        var known = cache?.Read(e.Model, want) ?? [];
        var missing = want.FindAll(t => !known.ContainsKey(t));
        if (missing.Count > 0)
        {
            var fresh = e.Embed(missing);
            for (var i = 0; i < missing.Count; i++) known[missing[i]] = fresh[i];
            if (keep) cache?.Write(e.Model, [.. missing.Select((t, i) => (t, fresh[i]))]);
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

    // Only a factor read from the article name is kept with the mapping; one from the
    // ingredient's piece weight is looked up at calculation time, so correcting the weight
    // corrects every case.
    static long? Packed(RuleSet rs, Ingredient ing, string unitCode, Pack? pack) =>
        Factors.Of(Scale.Of(rs, ing.Id), ing.Piece, unitCode, pack, null) is (var f, _, FactorSource.Pack) ? f : null;
}

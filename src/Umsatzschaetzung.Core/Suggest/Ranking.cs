using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

public sealed record Ranked(string IngredientId, int Confidence);

// Which wares a line reads like, best first, and how sure: the part of a suggestion a model
// answers. The exact hit, the cut and the mapping are the matcher's, whoever ranks.
public interface IRanking
{
    IReadOnlyList<Ranked> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count);
}

public sealed class EncoderRanking(IEncoder encoder, IEmbeddingCache? cache = null) : IRanking
{
    const double Mismatch = 0.1;
    const int MostLines = 4096;

    readonly Lock gate = new();

    // A RuleSet is known by its version, not by reference, so the version is the key: one
    // ranking belongs to one rule store and reindexes only once a save bumps it or a
    // case from another Gewerbe asks. Validity is a question of the invoice's date and
    // is asked per query, not baked into the index.
    long indexed = -1;
    string indexedGewerbe = "";
    string[] owner = [];
    Meta[] ware = [];
    Meta?[] rule = [];
    string[] wording = [];
    float[] vectors = [];

    // A line is asked about again with every selection and every mapping that changes the rules.
    readonly Dictionary<string, float[]> lines = new(StringComparer.Ordinal);

    public IReadOnlyList<Ranked> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count)
    {
        lock (gate)
        {
            Index(rs, gewerbe);
            var text = Matcher.Normal(line.Name);
            if (!lines.TryGetValue(text, out var query))
            {
                if (lines.Count >= MostLines) lines.Clear();
                lines[text] = query = Embed([text], keep: false)[0];
            }
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

            return [.. best
                .OrderByDescending(s => s.Value)
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .Take(count)
                .Select(s => new Ranked(s.Key, encoder.Confidence(s.Value)))];
        }
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
            text = Matcher.Normal(text);
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
            if (m.Confirmed && covered.TryGetValue(m.IngredientId, out var ing)) Add(ing, m.Meta, Matcher.Wording(m));
        }

        // A save bumps the version for every mapping the import proposes, and almost none
        // of them adds a wording: the vectors stay as they are, or what the last index holds
        // is carried over, not read again.
        if (!texts.SequenceEqual(wording, StringComparer.Ordinal))
        {
            var prior = new Dictionary<string, int>(wording.Length, StringComparer.Ordinal);
            for (var i = 0; i < wording.Length; i++) prior.TryAdd(wording[i], i);
            var fresh = texts.Where(t => !prior.ContainsKey(t)).Distinct(StringComparer.Ordinal).ToList();
            var embedded = fresh.Zip(Embed(fresh, keep: true)).ToDictionary(StringComparer.Ordinal);
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

    float[][] Embed(IReadOnlyList<string> texts, bool keep)
    {
        var want = texts.Distinct(StringComparer.Ordinal).ToList();
        var known = cache?.Read(encoder.Model, want) ?? [];
        var missing = want.FindAll(t => !known.ContainsKey(t));
        if (missing.Count > 0)
        {
            var fresh = encoder.Embed(missing);
            for (var i = 0; i < missing.Count; i++) known[missing[i]] = fresh[i];
            if (keep) cache?.Write(encoder.Model, [.. missing.Select((t, i) => (t, fresh[i]))]);
        }
        return [.. texts.Select(t => known[t])];
    }
}

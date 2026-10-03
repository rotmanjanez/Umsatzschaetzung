using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

// Every wording the wares of a Gewerbe are known under, as the encoder reads it, and its vector.
// With the encoder and its model this is all the embeddings warmed for the shipped rules hang on:
// Embeddings.targets warms them again when it changes, not when the ranking does.
public sealed class Wares(IEncoder encoder, IEmbeddingCache? cache = null)
{
    // A RuleSet is known by its version, not by reference, so the version is the key: one
    // index belongs to one rule store and is built again only once a save bumps it or a
    // case from another Gewerbe asks. Validity is a question of the invoice's date and
    // is asked per query, not baked into the index.
    long indexed = -1;
    string indexedGewerbe = "";
    string[] wording = [];

    public string[] Owner { get; private set; } = [];
    public Meta[] Ware { get; private set; } = [];
    public Meta?[] Rule { get; private set; } = [];
    public float[] Vectors { get; private set; } = [];

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

    // Only the wares of the case's Gewerbe are candidates: a Gaststätte is never offered
    // Blondierpulver. A ware is a product something says is bought: a category, an alias
    // or a confirmed mapping; one with none of them is only known as a dish or a cut, and
    // its name reads like the goods it is made of. Every name a ware is known under is its
    // own entry — the product is whichever of its wordings comes closest, not their average.
    public async Task Index(RuleSet rs, string gewerbe, CancellationToken ct)
    {
        if (indexed == rs.Version && indexedGewerbe == gewerbe) return;
        var mapped = rs.Mappings.Values.Where(m => m.Confirmed).Select(m => m.ProductId).ToHashSet(StringComparer.Ordinal);
        var covered = rs.Products.Values
            .Where(p => !string.IsNullOrEmpty(p.CategoryId)
                ? !rs.Categories.TryGetValue(p.CategoryId, out var c) || c.Covers(gewerbe)
                : p.Aliases.Count > 0 || mapped.Contains(p.Id))
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToDictionary(p => p.Id, StringComparer.Ordinal);

        var texts = new List<string>();
        var owners = new List<string>();
        var wares = new List<Meta>();
        var rules = new List<Meta?>();
        var seen = new HashSet<(string, string, Meta?)>();
        void Add(Product ware, Meta? by, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            text = Normal(text);
            if (by is not null && seen.Contains((ware.Id, text, null))) return;
            if (!seen.Add((ware.Id, text, by))) return;
            owners.Add(ware.Id);
            wares.Add(ware.Meta);
            rules.Add(by);
            texts.Add(text);
        }

        foreach (var ware in covered.Values)
        {
            Add(ware, null, ware.Name);
            foreach (var alias in ware.Aliases) Add(ware, null, alias);
        }
        // A confirmed mapping is a wording a human tied to a ware; the next line that
        // reads like it lands on the same ware without asking again.
        foreach (var id in rs.Mappings.Keys.Order(StringComparer.Ordinal))
        {
            var m = rs.Mappings[id];
            if (m.Confirmed && covered.TryGetValue(m.ProductId, out var ware)) Add(ware, m.Meta, Wording(m));
        }

        // A save bumps the version for every mapping the import proposes, and almost none
        // of them adds a wording: the vectors stay as they are, or what the last index holds
        // is carried over, not read again.
        if (!texts.SequenceEqual(wording, StringComparer.Ordinal))
        {
            var prior = new Dictionary<string, int>(wording.Length, StringComparer.Ordinal);
            for (var i = 0; i < wording.Length; i++) prior.TryAdd(wording[i], i);
            var fresh = texts.Where(t => !prior.ContainsKey(t)).Distinct(StringComparer.Ordinal).ToList();
            var embedded = fresh.Zip(await Embed(fresh, keep: true, ct)).ToDictionary(StringComparer.Ordinal);
            var next = new float[texts.Count * IEncoder.Width];
            for (var i = 0; i < texts.Count; i++)
            {
                var into = next.AsSpan(i * IEncoder.Width, IEncoder.Width);
                if (prior.TryGetValue(texts[i], out var at)) Vectors.AsSpan(at * IEncoder.Width, IEncoder.Width).CopyTo(into);
                else embedded[texts[i]].CopyTo(into);
            }
            Vectors = next;
            wording = [.. texts];
        }
        Owner = [.. owners];
        Ware = [.. wares];
        Rule = [.. rules];
        indexed = rs.Version;
        indexedGewerbe = gewerbe;
    }

    public async Task<float[][]> Embed(IReadOnlyList<string> texts, bool keep, CancellationToken ct)
    {
        var want = texts.Distinct(StringComparer.Ordinal).ToList();
        var known = cache?.Read(encoder.Model, want) ?? [];
        var missing = want.FindAll(t => !known.ContainsKey(t));
        if (missing.Count > 0)
        {
            var fresh = await encoder.Embed(missing, ct);
            for (var i = 0; i < missing.Count; i++) known[missing[i]] = fresh[i];
            if (keep) cache?.Write(encoder.Model, [.. missing.Select((t, i) => (t, fresh[i]))]);
        }
        return [.. texts.Select(t => known[t])];
    }
}

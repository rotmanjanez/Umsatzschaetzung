using Umsatzschätzung.Model;

namespace Umsatzschätzung.Suggest;

// What the confirmed mappings say a word means. Every confirmation is a
// (text, ingredient) observation, so the word "Pils" earns its link to
// Flaschenbier from the audits it appeared in rather than from a list someone
// wrote down. A supplier's own wording counts double, its own unreviewed
// guesses count a quarter, so the suggester cannot teach itself.
public sealed class Evidence
{
    const double SupplierWeight = 2.0;
    const double UnconfirmedWeight = 0.25;
    const double UnseenPrior = 1.0;

    readonly record struct Posting(string IngredientId, string? SupplierVatId, double Weight);

    readonly Dictionary<string, List<Posting>> postings = new(StringComparer.Ordinal);
    readonly Dictionary<string, double> idf = new(StringComparer.Ordinal);

    public Evidence(IEnumerable<ArticleMapping> log, IReadOnlySet<string> known)
    {
        foreach (var m in log)
        {
            if (Matcher.Wording(m) is not { } text || !known.Contains(m.IngredientId)) continue;
            var weight = m.Confirmed ? 1.0 : UnconfirmedWeight;
            foreach (var t in Terms(text))
            {
                if (!postings.TryGetValue(t, out var list)) postings[t] = list = [];
                list.Add(new Posting(m.IngredientId, m.SupplierVatId, weight));
            }
        }
        var total = postings.Values.SelectMany(p => p).Select(p => p.IngredientId).ToHashSet(StringComparer.Ordinal).Count;
        foreach (var (t, p) in postings)
            idf[t] = Math.Log(1.0 + (double)total / p.Select(x => x.IngredientId).ToHashSet(StringComparer.Ordinal).Count);
    }

    public Dictionary<string, double> Score(string text, string? supplierVatId)
    {
        var terms = Terms(text).Where(postings.ContainsKey).ToList();
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        if (terms.Count == 0) return scores;
        var mass = terms.Sum(t => idf[t]);
        foreach (var t in terms)
        {
            var weights = new Dictionary<string, double>(StringComparer.Ordinal);
            var seen = UnseenPrior;
            foreach (var p in postings[t])
            {
                var w = p.Weight * (p.SupplierVatId is not null && p.SupplierVatId == supplierVatId ? SupplierWeight : 1.0);
                weights[p.IngredientId] = weights.GetValueOrDefault(p.IngredientId) + w;
                seen += w;
            }
            foreach (var (id, w) in weights)
                scores[id] = scores.GetValueOrDefault(id) + idf[t] / mass * (w / seen);
        }
        return scores;
    }

    // Packaging is stripped first: it says nothing about which ingredient a line is,
    // and leaving it in is what makes "Pfand Kiste 20 x 0,5 l" look like the crate of
    // beer next to it.
    static IEnumerable<string> Terms(string text) =>
        Lexicon.Fold(PackSize.Strip(text))
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length >= 2)
            .Distinct(StringComparer.Ordinal);
}

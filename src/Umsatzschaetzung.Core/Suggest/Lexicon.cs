using System.Text;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

// Character n-grams over every known name of an ingredient — its own and those of
// the mappings pointing at it — ranked by how much of a name the invoice text covers.
// Asymmetric on purpose: an ingredient is a head noun and the line adds a brand, a
// pack size and a supplier's abbreviations around it.
public sealed class Lexicon
{
    const int Gram = 3;
    const double NameWeight = 0.75;

    readonly List<Ingredient> ingredients = [];
    readonly List<Dictionary<string, double>> grams = [];
    readonly List<double> mass = [];
    readonly Dictionary<string, double> idf = new(StringComparer.Ordinal);
    readonly double unseen;

    public Lexicon(IEnumerable<(Ingredient Ingredient, string Name)> names)
    {
        var sets = new List<HashSet<string>>();
        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (ing, name) in names)
        {
            var set = Grams(name);
            if (set.Count == 0) continue;
            ingredients.Add(ing);
            sets.Add(set);
            foreach (var g in set) df[g] = df.GetValueOrDefault(g) + 1;
        }
        var n = ingredients.Count;
        unseen = Idf(n, 1);
        foreach (var (g, c) in df) idf[g] = Idf(n, c);
        foreach (var set in sets)
        {
            var w = set.ToDictionary(g => g, g => idf[g], StringComparer.Ordinal);
            grams.Add(w);
            mass.Add(w.Values.Sum());
        }
    }

    static double Idf(int n, int df) => Math.Log(1.0 + (double)n / df);

    public Dictionary<string, double> Score(string text)
    {
        var scores = new Dictionary<string, double>(StringComparer.Ordinal);
        var query = Grams(text);
        if (query.Count == 0) return scores;
        var queryMass = query.Sum(g => idf.GetValueOrDefault(g, unseen));
        for (var i = 0; i < ingredients.Count; i++)
        {
            var shared = 0.0;
            foreach (var (g, w) in grams[i])
                if (query.Contains(g)) shared += w;
            if (shared <= 0) continue;
            var score = NameWeight * (shared / mass[i]) + (1 - NameWeight) * (shared / queryMass);
            var id = ingredients[i].Id;
            if (!scores.TryGetValue(id, out var best) || score > best) scores[id] = score;
        }
        return scores;
    }

    // Words are padded with blanks so a gram carries its word boundary: a three
    // letter name then has to appear as a whole word, not inside a longer one.
    internal static HashSet<string> Grams(string text)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in Fold(PackSize.Strip(text)).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var padded = " " + word + " ";
            for (var i = 0; i + Gram <= padded.Length; i++) set.Add(padded.Substring(i, Gram));
        }
        return set;
    }

    internal static string Fold(string text)
    {
        var b = new StringBuilder(text.Length + 8);
        var gap = true;
        foreach (var c in text.ToLowerInvariant())
        {
            var repl = c switch { 'ä' => "ae", 'ö' => "oe", 'ü' => "ue", 'ß' => "ss", _ => null };
            if (repl is null && c is (< 'a' or > 'z') and (< '0' or > '9')) { gap = true; continue; }
            if (gap && b.Length > 0) b.Append(' ');
            gap = false;
            if (repl is null) b.Append(c); else b.Append(repl);
        }
        return b.ToString();
    }
}

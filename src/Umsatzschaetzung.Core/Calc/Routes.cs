using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

// Ein Weg ist, woraus eine verkaufte Einheit entsteht: jedes Produkt auf dem Weg nach unten kommt vom
// eigenen Bestand oder aus seinem Rezept. Er nennt je Bestand die Menge in dessen Basiseinheit, je
// Bestand einmal gerundet.
internal static class Routes
{
    public const int Cap = 64;

    public static (List<Dictionary<string, long>> Routes, bool Cut) Of(RuleSet rs, Product p, IReadOnlySet<string> stocked)
    {
        var walker = new Walker(rs, stocked);
        var unit = Ratio.Of(Scale.ToBase(1, p.Unit));
        List<Dictionary<string, long>> output = [];
        foreach (var way in walker.Ways(p.Id))
        {
            var route = way.ToDictionary(w => w.Key, w => w.Value.Times(unit.Num, unit.Den).Rounded);
            if (!output.Exists(o => Same(o, route))) output.Add(route);
        }
        return (output, walker.Cut);
    }

    static bool Same(Dictionary<string, long> a, Dictionary<string, long> b) =>
        a.Count == b.Count && a.All(x => b.TryGetValue(x.Key, out var y) && y == x.Value);

    // Die Wege je Basiseinheit eines Produkts, einmal je Produkt gerechnet. Vorn steht der mit dem
    // meisten eigenen Bestand, hinten der, auf dem alles gemacht wird.
    sealed class Walker(RuleSet rs, IReadOnlySet<string> stocked)
    {
        readonly Dictionary<string, List<Dictionary<string, Ratio>>> done = [];
        readonly HashSet<string> open = [];

        public bool Cut { get; private set; }

        public List<Dictionary<string, Ratio>> Ways(string id)
        {
            if (done.TryGetValue(id, out var known)) return known;
            List<Dictionary<string, Ratio>> ways = stocked.Contains(id) ? [new() { [id] = Ratio.Of(1) }] : [];
            if (rs.Products.TryGetValue(id, out var p) && p.Recipe.Count > 0)
            {
                if (!open.Add(id)) throw Recipes.Cycle(p);
                var batch = Scale.ToBase(p.Batch, p.Unit);
                var options = p.Recipe.Select(l =>
                {
                    return Ways(l.PartId).Select(w => w.ToDictionary(x => x.Key, x => x.Value.Times(Scale.ToBase(l.Amount, l.Unit), batch))).ToList();
                }).ToList();
                open.Remove(id);
                ways.AddRange(Made(options, Cap - ways.Count));
            }
            done[id] = ways;
            return ways;
        }

        // Alle Verbindungen der Zeilen, solange es nicht mehr als room sind; sonst beide Enden und, was
        // am wenigsten von einem der beiden abweicht, damit jede Wahl jeder Zeile vorkommt.
        List<Dictionary<string, Ratio>> Made(List<List<Dictionary<string, Ratio>>> options, int room)
        {
            if (options.Exists(o => o.Count == 0)) return [];
            var sizes = options.Select(o => o.Count).ToArray();
            List<int[]> picks = [];
            long total = 1;
            foreach (var n in sizes) total = Math.Min(total * n, room + 1L);
            if (total <= room) picks.AddRange(All(sizes));
            else
            {
                Cut = true;
                var first = new int[sizes.Length];
                var last = sizes.Select(n => n - 1).ToArray();
                HashSet<string> seen = [string.Join(',', first), string.Join(',', last)];
                picks.Add(first);
                for (var k = 1; k <= sizes.Length && picks.Count < room - 1; k++)
                    foreach (var reference in new[] { first, last })
                        foreach (var pick in Deviations(reference, sizes, k, 0))
                        {
                            if (picks.Count == room - 1) break;
                            if (seen.Add(string.Join(',', pick))) picks.Add(pick);
                        }
                picks.Add(last);
            }
            return [.. picks.Select(pick =>
            {
                var way = new Dictionary<string, Ratio>();
                for (var i = 0; i < pick.Length; i++)
                    foreach (var (id, qty) in options[i][pick[i]])
                        way[id] = way.TryGetValue(id, out var have) ? have.Plus(qty) : qty;
                return way;
            })];
        }

        static IEnumerable<int[]> All(int[] sizes)
        {
            var pick = new int[sizes.Length];
            while (true)
            {
                yield return (int[])pick.Clone();
                var i = sizes.Length - 1;
                while (i >= 0 && ++pick[i] == sizes[i]) pick[i--] = 0;
                if (i < 0) yield break;
            }
        }

        static IEnumerable<int[]> Deviations(int[] reference, int[] sizes, int k, int from)
        {
            if (k == 0)
            {
                yield return (int[])reference.Clone();
                yield break;
            }
            for (var i = from; i <= reference.Length - k; i++)
                for (var j = 0; j < sizes[i]; j++)
                {
                    if (j == reference[i]) continue;
                    foreach (var pick in Deviations(reference, sizes, k - 1, i + 1))
                    {
                        pick[i] = j;
                        yield return pick;
                    }
                }
        }
    }
}

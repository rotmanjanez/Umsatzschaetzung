using System.Globalization;
using System.Text;
using System.Text.Json;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Eval;

// The article matcher over labelled product names. A row's expected ingredient is the
// one carrying its labelled product type as a name or an alias. Rows in a non-goods
// gruppe that no ingredient claims belong to the kein-Wareneinsatz category; all other
// rows without a claiming ingredient have no expectation and are only counted.
public static class Matching
{
    const string Gewerbe = "56101.0";
    const string NonGoodsCategory = "cat.kein.wareneinsatz";
    const int Shown = 40;
    const int Auto = 80;

    static readonly HashSet<string> NonGoods =
        ["Verpackung und Einweg", "Reinigung und Hygiene", "Nonfood sonstiges", "Pfand und Leergut", "Dienstleistung", "Unklar"];

    static readonly int[] Coverages = [25, 50, 75, 100];

    sealed record Row(string Name, string Produkt, string Gruppe, string Source);

    sealed record Scored(Row Row, string Top, int Confidence, bool Top1, bool Top5);

    public static int Run(string labels, string detail, string show)
    {
        var rs = RuleStore.Seed();
        using var matcher = new Matcher();
        var expected = Expectations(rs);
        var nonGoods = rs.Ingredients.Values.Where(i => i.CategoryId == NonGoodsCategory)
            .Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        var rows = Read(labels).ToList();
        var scored = new List<Scored>();
        var lines = new List<string>();
        var byGroup = new SortedDictionary<string, (int N, int Want, int Top1)>(StringComparer.Ordinal);
        var wrong = new Dictionary<string, int>(StringComparer.Ordinal);
        var started = DateTime.UtcNow;

        foreach (var row in rows)
        {
            var sugs = matcher.Suggest(rs, Gewerbe, null, new InvoiceLine { Name = row.Name, UnitCode = "" });
            var ings = sugs.ConvertAll(s => s.Mapping.IngredientId);
            var want = expected.GetValueOrDefault(Fold(row.Produkt))
                ?? (NonGoods.Contains(row.Gruppe) && nonGoods.Count > 0 ? nonGoods : null);
            var top = sugs.Count > 0 ? sugs[0] : null;
            var topName = top is null ? "" : rs.Ingredients[top.Mapping.IngredientId].Name;
            var top1 = want is not null && ings.Count > 0 && want.Contains(ings[0]);
            var top5 = want is not null && ings.Exists(want.Contains);
            if (want is not null) scored.Add(new Scored(row, topName, top?.Confidence ?? 0, top1, top5));

            var g = byGroup.GetValueOrDefault(row.Gruppe);
            byGroup[row.Gruppe] = (g.N + 1, g.Want + (want is null ? 0 : 1), g.Top1 + (top1 ? 1 : 0));
            if (want is not null && !top1 && top is { Confidence: >= Auto })
            {
                var key = $"{row.Produkt} -> {topName}";
                wrong[key] = wrong.GetValueOrDefault(key) + 1;
            }
            lines.Add(string.Join('\t', row.Source, row.Gruppe, row.Produkt, row.Name, topName, top?.Confidence ?? 0,
                want is null ? "-" : top1 ? "ok" : "wrong"));
            if (show != "" && row.Name.Contains(show, StringComparison.OrdinalIgnoreCase))
                Console.WriteLine($"{row.Name}\n  " + string.Join("\n  ",
                    sugs.Select(s => $"{s.Confidence,3} {rs.Ingredients[s.Mapping.IngredientId].Name}")));
        }

        var seconds = (DateTime.UtcNow - started).TotalSeconds;
        Console.WriteLine($"[{labels}, {rows.Count} rows, seed rules, Gewerbe {Gewerbe}, {seconds:F1} s]\n");
        Console.WriteLine($"{"gruppe",-34}{"n",7}{"erwartet",10}{"top1",8}   top1 of rows with an expectation");
        foreach (var (name, t) in byGroup)
            Console.WriteLine($"{name,-34}{t.N,7}{t.Want,10}{Rate(t.Top1, t.Want),8}");

        Console.WriteLine($"\nrows                 {rows.Count}");
        Console.WriteLine($"with an expectation  {scored.Count}   {Rate(scored.Count, rows.Count)} of all rows");
        Console.WriteLine($"top-1 accuracy       {Rate(scored.Count(s => s.Top1), scored.Count)}");
        Console.WriteLine($"top-5 accuracy       {Rate(scored.Count(s => s.Top5), scored.Count)}");

        var ranked = scored.OrderByDescending(s => s.Confidence)
            .ThenBy(s => s.Row.Name, StringComparer.Ordinal).ToList();
        Console.WriteLine("\ncoverage      rows   precision");
        foreach (var c in Coverages)
        {
            var take = ranked.Count * c / 100;
            Console.WriteLine($"{c,7}%{take,10}{Rate(ranked.Take(take).Count(s => s.Top1), take),12}");
        }

        var auto = ranked.FindAll(s => s.Confidence >= Auto);
        Console.WriteLine($"\nauto-map (>= {Auto})     share {Rate(auto.Count, scored.Count)}   precision {Rate(auto.Count(s => s.Top1), auto.Count)}");
        Console.WriteLine("\nwrong auto-maps, by rows:");
        foreach (var (p, n) in wrong.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal).Take(Shown))
            Console.WriteLine($"{n,6}  {p}");
        if (detail != "") File.WriteAllLines(detail, lines);
        return 0;
    }

    // Ein Produkttyp kann in mehreren Zutaten stecken — "Gouda" als Schnittkäse und als
    // geriebener Käse —, jede davon ist eine richtige Antwort.
    static Dictionary<string, HashSet<string>> Expectations(RuleSet rs)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var ing in rs.Ingredients.Values.Where(i => i.Meta.ValidOn(today)))
            foreach (var name in ing.Aliases.Prepend(ing.Name))
            {
                var key = Fold(name);
                if (key == "") continue;
                if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(ing.Id);
            }
        return map;
    }

    static string Fold(string text)
    {
        var b = new StringBuilder(text.Length);
        foreach (var c in text.ToLowerInvariant())
            switch (c)
            {
                case 'ä': b.Append("ae"); break;
                case 'ö': b.Append("oe"); break;
                case 'ü': b.Append("ue"); break;
                case 'ß': b.Append("ss"); break;
                case >= 'a' and <= 'z' or >= '0' and <= '9': b.Append(c); break;
            }
        return b.ToString();
    }

    static string Rate(int a, int b) => b == 0 ? "-" : (a / (double)b).ToString("F2", CultureInfo.InvariantCulture);

    static IEnumerable<Row> Read(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            using var doc = JsonDocument.Parse(line);
            var r = doc.RootElement;
            var label = r.GetProperty("label");
            yield return new Row(r.GetProperty("name").GetString()!, label.GetProperty("produkt").GetString()!,
                label.GetProperty("gruppe").GetString()!, r.GetProperty("source").GetString()!);
        }
    }
}

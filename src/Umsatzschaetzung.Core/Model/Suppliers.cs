namespace Umsatzschaetzung.Model;

// A supplier read under several names in one case, "Sommer", "WEIN GUT" and "Weingut Sommer",
// goes by the fullest of them. A part of a name, or a misreading of it, joins the name only
// when both sell an article in common, so two wineries stay apart. Only the fresh invoices are
// renamed: a name already in the case, perhaps typed by hand, is kept and taken over.
public static class Suppliers
{
    sealed class Group(string key)
    {
        public readonly string Key = key;
        public readonly List<Invoice> Invoices = [];
        public readonly HashSet<string> Items = [];
        public bool Fresh;
        public Group Root = null!;
    }

    public static bool Unify(Case c, IReadOnlySet<string> fresh)
    {
        var groups = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        foreach (var inv in c.Invoices)
        {
            var key = Key(inv.SupplierName);
            if (key == "") continue;
            if (!groups.TryGetValue(key, out var g)) groups[key] = g = new Group(key);
            g.Invoices.Add(inv);
            g.Fresh |= fresh.Contains(inv.Id);
            foreach (var l in inv.Lines) g.Items.UnionWith(Items(l));
        }
        foreach (var g in groups.Values) g.Root = g;

        foreach (var g in groups.Values.Where(g => g.Fresh))
        {
            Group? best = null;
            var most = 0;
            foreach (var o in groups.Values)
            {
                if (o == g || !Joins(g.Key, o.Key)) continue;
                var shared = g.Items.Count(o.Items.Contains);
                if (shared > most) (best, most) = (o, shared);
            }
            if (best is not null) Find(g).Root = Find(best);
        }

        var changed = false;
        foreach (var cluster in groups.Values.GroupBy(Find))
        {
            var members = cluster.ToList();
            var name = Name(members, fresh);
            foreach (var inv in members.SelectMany(m => m.Invoices))
                if (fresh.Contains(inv.Id) && inv.SupplierName != name)
                {
                    inv.SupplierName = name;
                    changed = true;
                }
        }
        return changed;
    }

    static Group Find(Group g)
    {
        while (g.Root != g) g = g.Root = g.Root.Root;
        return g;
    }

    // Whether a may take b's name: a is a part of it, or b read with a letter or two off.
    static bool Joins(string a, string b) =>
        a.Length >= 4 && b.Length > a.Length && b.Contains(a, StringComparison.Ordinal)
        || Math.Min(a.Length, b.Length) >= 6 && Distance(a, b) <= Math.Min(a.Length, b.Length) / 6;

    // A name the case already has, else the fullest, one no other contains; an e-invoice
    // states it, else the one on the most invoices.
    static string Name(List<Group> members, IReadOnlySet<string> fresh)
    {
        var kept = members.Where(g => g.Invoices.Any(i => !fresh.Contains(i.Id))).ToList();
        if (kept.Count > 0) members = kept;
        var full = members.Where(g => !members.Any(o => o != g && o.Key.Length > g.Key.Length && o.Key.Contains(g.Key, StringComparison.Ordinal)));
        var pick = full
            .OrderByDescending(g => g.Invoices.Any(i => i.Source != Source.Scan))
            .ThenByDescending(g => g.Invoices.Count)
            .ThenByDescending(g => g.Key.Length)
            .First();
        return pick.Invoices
            .Where(i => kept.Count == 0 || !fresh.Contains(i.Id))
            .GroupBy(i => i.SupplierName)
            .OrderByDescending(s => s.Any(i => i.Source != Source.Scan))
            .ThenByDescending(s => s.Count())
            .ThenByDescending(s => s.Key.Any(char.IsLower))
            .ThenBy(s => s.Key, StringComparer.Ordinal)
            .First().Key;
    }

    static string Key(string name) =>
        new([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);

    static IEnumerable<string> Items(InvoiceLine l)
    {
        if (!string.IsNullOrEmpty(l.SellerArticleId)) yield return "a:" + l.SellerArticleId;
        if (!string.IsNullOrEmpty(l.Gtin)) yield return "g:" + l.Gtin;
        if (ArticleName.Canonical(l.Name) is { Length: > 0 } n) yield return "n:" + n;
    }

    static int Distance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1], prev[j]) + 1, prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}

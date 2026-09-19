namespace Umsatzschaetzung.Eval;

// difflib.SequenceMatcher(None, a, b).ratio(), character-wise. The line matcher's threshold
// was tuned against those numbers, so an approximation would silently move every match.
static class Similarity
{
    public static double Ratio(string a, string b)
    {
        var total = a.Length + b.Length;
        return total == 0 ? 1.0 : 2.0 * Matched(a, b) / total;
    }

    static int Matched(string a, string b)
    {
        var index = Index(b);
        var matched = 0;
        var queue = new Stack<(int Alo, int Ahi, int Blo, int Bhi)>();
        queue.Push((0, a.Length, 0, b.Length));
        while (queue.Count > 0)
        {
            var (alo, ahi, blo, bhi) = queue.Pop();
            var (i, j, k) = Longest(a, b, index, alo, ahi, blo, bhi);
            if (k == 0) continue;
            matched += k;
            if (alo < i && blo < j) queue.Push((alo, i, blo, j));
            if (i + k < ahi && j + k < bhi) queue.Push((i + k, ahi, j + k, bhi));
        }
        return matched;
    }

    // difflib's autojunk: in a b of 200 characters or more, anything occurring in more than
    // one percent of it stops seeding matches (it still extends them).
    static Dictionary<char, List<int>> Index(string b)
    {
        var index = new Dictionary<char, List<int>>();
        for (var j = 0; j < b.Length; j++)
        {
            if (!index.TryGetValue(b[j], out var at)) index[b[j]] = at = [];
            at.Add(j);
        }
        if (b.Length < 200) return index;
        var limit = b.Length / 100 + 1;
        foreach (var popular in index.Where(e => e.Value.Count > limit).Select(e => e.Key).ToList())
            index.Remove(popular);
        return index;
    }

    static (int I, int J, int K) Longest(string a, string b, Dictionary<char, List<int>> index,
        int alo, int ahi, int blo, int bhi)
    {
        int besti = alo, bestj = blo, best = 0;
        var lengths = new Dictionary<int, int>();
        for (var i = alo; i < ahi; i++)
        {
            var next = new Dictionary<int, int>();
            if (index.TryGetValue(a[i], out var at))
                foreach (var j in at)
                {
                    if (j < blo) continue;
                    if (j >= bhi) break;
                    var k = lengths.GetValueOrDefault(j - 1) + 1;
                    next[j] = k;
                    if (k > best) (besti, bestj, best) = (i - k + 1, j - k + 1, k);
                }
            lengths = next;
        }
        while (besti > alo && bestj > blo && a[besti - 1] == b[bestj - 1])
        {
            besti--;
            bestj--;
            best++;
        }
        while (besti + best < ahi && bestj + best < bhi && a[besti + best] == b[bestj + best]) best++;
        return (besti, bestj, best);
    }
}

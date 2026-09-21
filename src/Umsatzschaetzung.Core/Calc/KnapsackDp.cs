namespace Umsatzschaetzung.Calc;

sealed class DpProduct(string id, int dims)
{
    public string Id { get; } = id;
    public long[] Weight { get; } = new long[dims];
    public long Bound { get; set; } = -1;
    public long Value { get; set; }
}

sealed class DpPiece(int item, long count, int dims, long value)
{
    public int Item { get; } = item;
    public long Count { get; } = count;
    public long Offset { get; set; }
    public long[] Weight { get; } = new long[dims];
    public long Value { get; } = value;
    public ulong[] Taken { get; set; } = [];
}

sealed partial class Problem
{
    public void SolveShared(KnapsackResult res)
    {
        List<string> candidates = [], independent = [];
        foreach (var p in Free)
        {
            if (Bound[p] == 0 || !UsesShared(p)) independent.Add(p);
            else candidates.Add(p);
        }
        foreach (var p in independent) Portions[p] = Math.Max(Bound[p], 0);
        if (candidates.Count == 0) return;
        Shared.RemoveAll(i => !candidates.Exists(p => AmountOf(p, i) > 0));
        if (Shared.Count > Knapsack.MaxDpDimensions)
        {
            Greedy(candidates, res);
            return;
        }

        var capQ = new long[Shared.Count];
        var gcds = new long[Shared.Count];
        for (var d = 0; d < Shared.Count; d++)
        {
            var i = Shared[d];
            long g = 0;
            foreach (var p in candidates) g = Knapsack.Gcd(g, AmountOf(p, i));
            gcds[d] = g;
            capQ[d] = Remaining[i] / g;
        }
        if (!ChooseGrid(capQ, Knapsack.StateBudget, out var factor, out var states))
        {
            Greedy(candidates, res);
            return;
        }
        var capG = new long[capQ.Length];
        for (var d = 0; d < capQ.Length; d++)
        {
            capG[d] = capQ[d] / factor;
            if (capG[d] == 0)
            {
                Greedy(candidates, res);
                return;
            }
        }

        var items = new List<DpProduct>(candidates.Count);
        foreach (var p in candidates)
        {
            var it = new DpProduct(p, Shared.Count) { Value = checked(Value[p] * factor) };
            if (Bound[p] >= 0) it.Bound = Bound[p] / factor;
            for (var d = 0; d < Shared.Count; d++)
            {
                var w = AmountOf(p, Shared[d]) / gcds[d];
                it.Weight[d] = w;
                if (w == 0) continue;
                var limit = capG[d] / w;
                if (it.Bound < 0 || limit < it.Bound) it.Bound = limit;
            }
            _ = checked(it.Value * it.Bound);
            if (it.Bound > 0) items.Add(it);
        }
        res.Grid = factor;
        res.States = states;
        var batches = RunDp(capG, states, items);
        foreach (var p in candidates) Portions[p] = 0;
        for (var k = 0; k < items.Count; k++)
        {
            Portions[items[k].Id] = batches[k] * factor;
            Consume(items[k].Id, Portions[items[k].Id]);
        }
        if (factor > 1) Fill(candidates);
    }

    void Consume(string p, long x)
    {
        foreach (var (i, a) in Amount[p]) Remaining[i] = Remaining.GetValueOrDefault(i) - a * x;
        var b = Bound.GetValueOrDefault(p);
        if (b >= 0) Bound[p] = b - x;
    }

    bool UsesShared(string p)
    {
        foreach (var i in Shared)
            if (AmountOf(p, i) > 0) return true;
        return false;
    }

    static bool ChooseGrid(long[] capQ, long budget, out long factor, out long states)
    {
        factor = 0;
        states = 0;
        if (budget < 1) return false;
        long hi = 1;
        foreach (var c in capQ) hi = Math.Max(hi, c + 1);
        long lo = 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (StateCount(capQ, mid, out var n) && n <= budget) hi = mid;
            else lo = mid + 1;
        }
        if (!StateCount(capQ, lo, out var count) || count > budget) return false;
        factor = lo;
        states = count;
        return true;
    }

    static bool StateCount(long[] capQ, long factor, out long n)
    {
        n = 1;
        foreach (var c in capQ)
            if (!Knapsack.TryMul(n, c / factor + 1, out n))
            {
                n = 0;
                return false;
            }
        return true;
    }

    static long[] RunDp(long[] capG, long states, List<DpProduct> items)
    {
        var dims = capG.Length;
        var stride = new long[dims];
        long s = 1;
        for (var d = 0; d < dims; d++)
        {
            stride[d] = s;
            s *= capG[d] + 1;
        }
        var words = (states + 63) / 64;

        List<DpPiece> pieces = [];
        for (var k = 0; k < items.Count; k++)
        {
            var it = items[k];
            var rest = it.Bound;
            for (long c = 1; rest > 0; c *= 2)
            {
                if (c > rest) c = rest;
                rest -= c;
                var piece = new DpPiece(k, c, dims, it.Value * c);
                var fits = true;
                for (var d = 0; d < dims; d++)
                {
                    piece.Weight[d] = it.Weight[d] * c;
                    if (piece.Weight[d] > capG[d]) fits = false;
                    piece.Offset += piece.Weight[d] * stride[d];
                }
                if (fits)
                {
                    piece.Taken = new ulong[words];
                    pieces.Add(piece);
                }
            }
        }

        var best = new long[states];
        var coords = new long[dims];
        foreach (var piece in pieces)
        {
            Array.Copy(capG, coords, dims);
            for (var st = states - 1; st >= 0; st--)
            {
                if (FitsWeight(coords, piece.Weight))
                {
                    var v = best[st - piece.Offset] + piece.Value;
                    if (v > best[st])
                    {
                        best[st] = v;
                        piece.Taken[st >> 6] |= 1UL << (int)(st & 63);
                    }
                }
                for (var d = 0; d < dims; d++)
                {
                    coords[d]--;
                    if (coords[d] >= 0) break;
                    coords[d] = capG[d];
                }
            }
        }

        var x = new long[items.Count];
        s = states - 1;
        for (var pi = pieces.Count - 1; pi >= 0; pi--)
        {
            var piece = pieces[pi];
            if ((piece.Taken[s >> 6] & (1UL << (int)(s & 63))) != 0)
            {
                x[piece.Item] += piece.Count;
                s -= piece.Offset;
            }
        }
        return x;
    }

    static bool FitsWeight(long[] coords, long[] weight)
    {
        for (var d = 0; d < coords.Length; d++)
            if (coords[d] < weight[d]) return false;
        return true;
    }

    void Greedy(List<string> candidates, KnapsackResult res)
    {
        res.Approximate = true;
        res.Grid = 1;
        res.States = 0;
        foreach (var p in candidates) Portions[p] = 0;
        Fill(candidates);
    }

    void Fill(List<string> candidates)
    {
        var scarce = Shared[0];
        var scarceDemand = Demand(scarce, candidates);
        foreach (var i in Shared.Skip(1))
        {
            var demand = Demand(i, candidates);
            if (Knapsack.RatioLess(Remaining[i], demand, Remaining[scarce], scarceDemand))
                (scarce, scarceDemand) = (i, demand);
        }

        bool Less(string pa, string pb)
        {
            var (aa, ab) = (AmountOf(pa, scarce), AmountOf(pb, scarce));
            if (aa == 0 && ab == 0) return Value[pa] > Value[pb];
            if (aa == 0) return true;
            if (ab == 0) return false;
            return Knapsack.RatioLess(Value[pb], ab, Value[pa], aa);
        }
        var order = candidates.OrderBy(p => p, Comparer<string>.Create((a, b) => Less(a, b) ? -1 : Less(b, a) ? 1 : 0)).ToList();

        foreach (var p in order)
        {
            var x = Bound[p];
            foreach (var (i, a) in Amount[p])
            {
                var limit = Remaining.GetValueOrDefault(i) / a;
                if (x < 0 || limit < x) x = limit;
            }
            if (x < 0) x = 0;
            Portions[p] = Portions.GetValueOrDefault(p) + x;
            Consume(p, x);
        }
    }

    long Demand(string i, List<string> products)
    {
        long d = 0;
        foreach (var p in products) d += AmountOf(p, i);
        return Math.Max(d, 1);
    }
}

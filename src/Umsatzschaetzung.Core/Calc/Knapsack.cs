using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

internal sealed record Component(List<string> Products, List<string> Supply);

// Eine Spalte ist ein Weg eines verkauften Produkts; seine Portionen sind die Summe über seine Spalten.
internal sealed record Column(string ProductId, Dictionary<string, long> Amount);

internal sealed class KnapsackInput
{
    public Dictionary<string, long> Capacity { get; } = [];
    // Die Spalten eines Produkts stehen beieinander, die vom eigenen Bestand vorn.
    public List<Column> Columns { get; } = [];
    public Dictionary<string, long> UnitCost { get; } = [];
    public Dictionary<string, long> Pinned { get; } = [];
}

internal sealed class KnapsackResult
{
    public long[] Portions { get; set; } = [];
    public Dictionary<string, long> Leftover { get; set; } = [];
    public List<string> Binding { get; } = [];
    public long Grid { get; set; }
    public long States { get; set; }
    public bool Approximate { get; set; }
}

internal static class Knapsack
{
    public const long StateBudget = 1_000_000;
    public const int MaxDpDimensions = 4;

    public static List<Component> Components(List<string> products, Dictionary<string, List<Dictionary<string, long>>> routes, IEnumerable<string> supply)
    {
        var nodes = new List<string>(supply);
        nodes.Sort(StringComparer.Ordinal);

        var supplyIndex = new Dictionary<string, int>(nodes.Count);
        for (var i = 0; i < nodes.Count; i++) supplyIndex[nodes[i]] = products.Count + i;
        var uf = new UnionFind(products.Count + nodes.Count);
        for (var pi = 0; pi < products.Count; pi++)
            foreach (var route in routes[products[pi]])
                foreach (var id in route.Keys)
                    if (supplyIndex.TryGetValue(id, out var si)) uf.Union(pi, si);

        var groups = new Dictionary<int, Component>();
        List<int> roots = [];
        Component Group(int node)
        {
            var r = uf.Find(node);
            if (!groups.TryGetValue(r, out var c))
            {
                c = new([], []);
                groups[r] = c;
                roots.Add(r);
            }
            return c;
        }
        for (var pi = 0; pi < products.Count; pi++) Group(pi).Products.Add(products[pi]);
        for (var si = 0; si < nodes.Count; si++) Group(products.Count + si).Supply.Add(nodes[si]);
        return [.. roots.Select(r => groups[r])];
    }

    public static KnapsackResult Solve(KnapsackInput input)
    {
        var pr = new Problem(input);
        foreach (var (id, q) in input.Capacity)
        {
            pr.Supply.Add(id);
            pr.Remaining[id] = Math.Max(q, 0);
        }
        pr.Supply.Sort(StringComparer.Ordinal);
        foreach (var col in input.Columns) pr.Amount.Add(col.Amount);

        pr.ApplyPinned();
        pr.ComputeValues();
        pr.ReduceDimensions();

        var res = new KnapsackResult { Grid = 1 };
        if (pr.Shared.Count > 0) pr.SolveShared(res);
        foreach (var k in pr.Free)
            if (!pr.Portions.ContainsKey(k)) pr.Portions[k] = Math.Max(pr.Bound[k], 0);
        return pr.Finish(res);
    }

    public static bool TryMul(long a, long b, out long r)
    {
        r = 0;
        if (a == 0 || b == 0) return true;
        var product = (UInt128)(ulong)a * (ulong)b;
        if (product > long.MaxValue) return false;
        r = (long)product;
        return true;
    }

    public static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }

    public static bool RatioLess(long a, long b, long c, long d) =>
        (UInt128)(ulong)a * (ulong)d < (UInt128)(ulong)c * (ulong)b;
}

sealed class UnionFind(int n)
{
    readonly int[] parent = [.. Enumerable.Range(0, n)];

    public int Find(int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];
            x = parent[x];
        }
        return x;
    }

    public void Union(int a, int b)
    {
        var (ra, rb) = (Find(a), Find(b));
        if (ra == rb) return;
        if (ra < rb) parent[rb] = ra;
        else parent[ra] = rb;
    }
}

sealed partial class Problem(KnapsackInput input)
{
    public KnapsackInput In { get; } = input;
    public List<string> Supply { get; } = [];
    public List<Dictionary<string, long>> Amount { get; } = [];
    public Dictionary<string, long> Remaining { get; } = [];
    public Dictionary<int, long> Portions { get; } = [];
    public List<int> Free { get; } = [];
    public Dictionary<int, long> Bound { get; } = [];
    public Dictionary<int, long> Value { get; } = [];
    public List<string> Shared { get; } = [];

    long AmountOf(int k, string i) => Amount[k].GetValueOrDefault(i);

    // Eine Vorgabe gilt für das Produkt; sie füllt seine Wege der Reihe nach, der letzte nimmt den Rest.
    public void ApplyPinned()
    {
        var open = new Dictionary<string, long>(In.Pinned);
        for (var k = 0; k < Amount.Count; k++)
        {
            var product = In.Columns[k].ProductId;
            if (!open.TryGetValue(product, out var n))
            {
                Free.Add(k);
                continue;
            }
            if (n < 0) throw new InvalidOperationException($"knapsack: negative Pin für Produkt {product}");
            var last = k + 1 == Amount.Count || In.Columns[k + 1].ProductId != product;
            var x = last ? n : Math.Min(n, Fits(k));
            open[product] = n - x;
            Portions[k] = x;
            foreach (var (i, a) in Amount[k])
            {
                var use = checked(a * x);
                if (Remaining.TryGetValue(i, out var have)) Remaining[i] = Math.Max(have - use, 0);
            }
        }
    }

    long Fits(int k)
    {
        var x = long.MaxValue;
        foreach (var (i, a) in Amount[k]) x = Math.Min(x, Remaining.GetValueOrDefault(i) / a);
        return x;
    }

    public void ComputeValues()
    {
        foreach (var k in Free)
        {
            long v = 0;
            foreach (var (i, a) in Amount[k])
                v = checked(v + a * Math.Max(In.UnitCost.GetValueOrDefault(i), 1));
            Value[k] = v;
        }
    }

    public void ReduceDimensions()
    {
        var users = new Dictionary<string, List<int>>();
        foreach (var k in Free)
        {
            Bound[k] = -1;
            foreach (var i in Amount[k].Keys)
            {
                if (!users.TryGetValue(i, out var list)) users[i] = list = [];
                list.Add(k);
            }
        }
        foreach (var k in Free)
            foreach (var (i, a) in Amount[k])
            {
                if (!Remaining.TryGetValue(i, out var have) || have < a) Tighten(k, 0);
                else if (users[i].Count == 1) Tighten(k, have / a);
            }
        foreach (var i in Supply)
            if (users.TryGetValue(i, out var list) && list.Count >= 2) Shared.Add(i);
    }

    void Tighten(int k, long b)
    {
        if (Bound[k] < 0 || b < Bound[k]) Bound[k] = b;
    }

    public KnapsackResult Finish(KnapsackResult res)
    {
        res.Portions = new long[Amount.Count];
        res.Leftover = new Dictionary<string, long>(Supply.Count);
        var used = new Dictionary<string, long>();
        var minAmount = new Dictionary<string, long>();
        for (var k = 0; k < Amount.Count; k++)
        {
            var x = res.Portions[k] = Portions[k];
            foreach (var (i, a) in Amount[k])
            {
                used[i] = checked(used.GetValueOrDefault(i) + a * x);
                if (!minAmount.TryGetValue(i, out var m) || a < m) minAmount[i] = a;
            }
        }
        foreach (var i in Supply)
        {
            var left = Math.Max(In.Capacity.GetValueOrDefault(i) - used.GetValueOrDefault(i), 0);
            res.Leftover[i] = left;
            if (minAmount.TryGetValue(i, out var m) && left < m) res.Binding.Add(i);
        }
        return res;
    }
}

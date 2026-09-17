using Umsatzschätzung.Model;

namespace Umsatzschätzung.Calc;

internal sealed record Component(List<string> Products, List<string> Ingredients);

internal sealed class KnapsackInput
{
    public Dictionary<string, long> Capacity { get; } = [];
    public Dictionary<string, List<RecipeLine>> Recipe { get; } = [];
    public Dictionary<string, long> UnitCost { get; } = [];
    public Dictionary<string, long> Pinned { get; } = [];
}

internal sealed class KnapsackResult
{
    public Dictionary<string, long> Portions { get; set; } = [];
    public Dictionary<string, long> Leftover { get; set; } = [];
    public List<string> Binding { get; } = [];
    public long Grid { get; set; }
    public long States { get; set; }
    public bool Approximate { get; set; }
}

internal static class Knapsack
{
    public const long StateBudget = 5_000_000;
    public const int MaxDpDimensions = 4;

    public static List<Component> Components(RuleSet rs, List<string> products, IEnumerable<string> ingredients)
    {
        var ings = new List<string>(ingredients);
        ings.Sort(StringComparer.Ordinal);

        var ingredientIndex = new Dictionary<string, int>(ings.Count);
        for (var i = 0; i < ings.Count; i++) ingredientIndex[ings[i]] = products.Count + i;
        var uf = new UnionFind(products.Count + ings.Count);
        for (var pi = 0; pi < products.Count; pi++)
            if (rs.Products.TryGetValue(products[pi], out var p))
                foreach (var line in p.Recipe)
                    if (ingredientIndex.TryGetValue(line.IngredientId, out var ii)) uf.Union(pi, ii);

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
        for (var ii = 0; ii < ings.Count; ii++) Group(products.Count + ii).Ingredients.Add(ings[ii]);
        return [.. roots.Select(r => groups[r])];
    }

    public static KnapsackResult Solve(KnapsackInput input)
    {
        var pr = new Problem(input);
        pr.Products.AddRange(input.Recipe.Keys);
        pr.Products.Sort(StringComparer.Ordinal);
        foreach (var (id, q) in input.Capacity)
        {
            pr.Ingredients.Add(id);
            pr.Remaining[id] = Math.Max(q, 0);
        }
        pr.Ingredients.Sort(StringComparer.Ordinal);
        foreach (var p in pr.Products)
        {
            var lines = new Dictionary<string, long>();
            foreach (var l in input.Recipe[p])
            {
                var amount = Scale.ToBase(l.Amount, l.Unit);
                if (amount > 0) lines[l.IngredientId] = lines.GetValueOrDefault(l.IngredientId) + amount;
            }
            pr.Amount[p] = lines;
        }

        pr.ApplyPinned();
        pr.ComputeValues();
        pr.ReduceDimensions();

        var res = new KnapsackResult { Grid = 1 };
        if (pr.Shared.Count > 0) pr.SolveShared(res);
        foreach (var p in pr.Free)
            if (!pr.Portions.ContainsKey(p)) pr.Portions[p] = Math.Max(pr.Bound[p], 0);
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
    public List<string> Products { get; } = [];
    public List<string> Ingredients { get; } = [];
    public Dictionary<string, Dictionary<string, long>> Amount { get; } = [];
    public Dictionary<string, long> Remaining { get; } = [];
    public Dictionary<string, long> Portions { get; } = [];
    public List<string> Free { get; } = [];
    public Dictionary<string, long> Bound { get; } = [];
    public Dictionary<string, long> Value { get; } = [];
    public List<string> Shared { get; } = [];

    long AmountOf(string p, string i) => Amount[p].GetValueOrDefault(i);

    public void ApplyPinned()
    {
        foreach (var p in Products)
        {
            if (!In.Pinned.TryGetValue(p, out var n))
            {
                Free.Add(p);
                continue;
            }
            if (n < 0) throw new InvalidOperationException($"knapsack: negative Pin für Produkt {p}");
            Portions[p] = n;
            foreach (var i in Amount[p].Keys)
            {
                var use = checked(Amount[p][i] * n);
                if (Remaining.TryGetValue(i, out var have)) Remaining[i] = Math.Max(have - use, 0);
            }
        }
    }

    public void ComputeValues()
    {
        foreach (var p in Free)
        {
            long v = 0;
            foreach (var i in Amount[p].Keys)
                v = checked(v + Amount[p][i] * Math.Max(In.UnitCost.GetValueOrDefault(i), 1));
            Value[p] = v;
        }
    }

    public void ReduceDimensions()
    {
        var users = new Dictionary<string, List<string>>();
        foreach (var p in Free)
        {
            Bound[p] = -1;
            foreach (var i in Amount[p].Keys)
            {
                if (!users.TryGetValue(i, out var list)) users[i] = list = [];
                list.Add(p);
            }
        }
        foreach (var p in Free)
            foreach (var (i, a) in Amount[p])
            {
                if (!Remaining.TryGetValue(i, out var have)) Tighten(p, 0);
                else if (users[i].Count == 1) Tighten(p, have / a);
            }
        foreach (var i in Ingredients)
            if (users.TryGetValue(i, out var list) && list.Count >= 2) Shared.Add(i);
    }

    void Tighten(string p, long b)
    {
        if (Bound[p] < 0 || b < Bound[p]) Bound[p] = b;
    }

    public KnapsackResult Finish(KnapsackResult res)
    {
        res.Portions = Portions;
        res.Leftover = new Dictionary<string, long>(Ingredients.Count);
        var used = new Dictionary<string, long>();
        var minAmount = new Dictionary<string, long>();
        foreach (var p in Products)
        {
            var x = Portions[p];
            foreach (var (i, a) in Amount[p])
            {
                used[i] = checked(used.GetValueOrDefault(i) + a * x);
                if (!minAmount.TryGetValue(i, out var m) || a < m) minAmount[i] = a;
            }
        }
        foreach (var i in Ingredients)
        {
            var left = Math.Max(In.Capacity.GetValueOrDefault(i) - used.GetValueOrDefault(i), 0);
            res.Leftover[i] = left;
            if (minAmount.TryGetValue(i, out var m) && left < m) res.Binding.Add(i);
        }
        foreach (var p in Products)
            if (Portions[p] == 0 && !In.Pinned.ContainsKey(p)) Portions.Remove(p);
        return res;
    }
}

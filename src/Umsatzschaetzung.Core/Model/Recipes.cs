using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Umsatzschaetzung.Model;

// Die Rezeptur der Prüfung ersetzt die des Katalogs, wo immer das Produkt vorkommt, auch im Rezept
// eines anderen, und die Zuordnungen der Prüfung kommen zu denen der Regeln.
public static class Recipes
{
    public static RuleSet Effective(Case c, RuleSet rs)
    {
        rs = rs.With(c.Mappings);
        Dictionary<string, Product>? products = null;
        foreach (var cp in c.Products)
        {
            if (cp.Recipe?.Where(l => Fits(rs, l)).ToList() is not { Count: > 0 } own || !rs.Products.TryGetValue(cp.ProductId, out var p)) continue;
            products ??= new Dictionary<string, Product>(rs.Products);
            products[p.Id] = new Product
            {
                Id = p.Id, Name = p.Name, Unit = p.Unit, Batch = p.Batch, CategoryId = p.CategoryId,
                Aliases = p.Aliases, Piece = p.Piece, Recipe = own, Meta = p.Meta,
            };
        }
        if (products is null) return rs;
        return new RuleSet
        {
            Store = rs.Store,
            Version = rs.Version,
            Categories = rs.Categories,
            Mappings = rs.Mappings,
            Products = products,
            YieldRules = rs.YieldRules,
            Gewerbezweige = rs.Gewerbezweige,
            Templates = rs.Templates,
        };
    }

    // Was eine Einheit verbraucht, wenn alles mit Rezept selbst gemacht wird: die Produkte ohne Rezept in
    // ihrer Basiseinheit, je Produkt einmal gerundet. Ein Produkt ohne Rezept verbraucht sich selbst.
    public static List<PartLine> Sold(RuleSet rs, Product p)
    {
        var unit = Scale.ToBase(1, p.Unit);
        return [.. Leaves(rs, p.Id, Scale.Of(p) ?? Unit.Piece, [], [])
            .Select(a => new PartLine { PartId = a.Key, Amount = a.Value.Qty.Times(unit, 1).Rounded, Unit = Scale.Code(a.Value.Unit) })];
    }

    // Je Basiseinheit des Produkts, einmal je Produkt gerechnet.
    static Dictionary<string, (Ratio Qty, Unit Unit)> Leaves(RuleSet rs, string id, Unit unit,
        Dictionary<string, Dictionary<string, (Ratio Qty, Unit Unit)>> done, HashSet<string> open)
    {
        if (done.TryGetValue(id, out var known)) return known;
        var output = new Dictionary<string, (Ratio Qty, Unit Unit)>();
        if (!rs.Products.TryGetValue(id, out var p) || p.Recipe.Count == 0) output[id] = (Ratio.Of(1), unit);
        else
        {
            if (!open.Add(id)) throw Cycle(p);
            var batch = Scale.ToBase(p.Batch, p.Unit);
            foreach (var l in p.Recipe)
            {
                var lineUnit = Units.Lookup(l.Unit)?.Base ?? Unit.Piece;
                var parts = Leaves(rs, l.PartId, lineUnit, done, open);
                foreach (var (part, (qty, partUnit)) in parts)
                {
                    var add = qty.Times(Scale.ToBase(l.Amount, l.Unit), batch);
                    output[part] = output.TryGetValue(part, out var have) ? (have.Qty.Plus(add), have.Unit) : (add, partUnit);
                }
            }
            open.Remove(id);
        }
        done[id] = output;
        return output;
    }

    // A line counts its part in the unit the part is counted in; one written before the part changed unit does not.
    public static bool Fits(RuleSet rs, PartLine l) =>
        rs.Products.TryGetValue(l.PartId, out var part) && Scale.Of(part) is { } counted && Units.Lookup(l.Unit)?.Base == counted;

    public static InvalidOperationException Cycle(Product p) => new($"Rezept „{p.Name}“ enthält sich selbst");

    // Jedes Produkt, das in einem der genannten steckt, sie selbst eingeschlossen.
    public static HashSet<string> Reachable(RuleSet rs, IEnumerable<string> ids)
    {
        HashSet<string> seen = [];
        var stack = new Stack<string>(ids);
        while (stack.TryPop(out var id))
        {
            if (!seen.Add(id) || !rs.Products.TryGetValue(id, out var p)) continue;
            foreach (var l in p.Recipe) stack.Push(l.PartId);
        }
        return seen;
    }

    public static bool Adjusted(Case c, string productId) =>
        c.Products.Find(p => p.ProductId == productId)?.Recipe is { Count: > 0 };

    public static bool Stale(CaseProduct cp, RuleSet rs) =>
        cp.Recipe is { Count: > 0 } && rs.Products.TryGetValue(cp.ProductId, out var p) && Basis(p) != cp.RecipeBasis;

    // Hängt nur an den Zeilen, nicht an der Regel-Datenbank: eine Falldatei trägt sie mit an ein anderes Amt.
    public static long Basis(Product p)
    {
        var text = new StringBuilder().Append(p.Batch).Append('\n');
        foreach (var l in p.Recipe) text.Append(l.PartId).Append('\t').Append(l.Amount).Append('\t').Append(l.Unit).Append('\n');
        return BinaryPrimitives.ReadInt64LittleEndian(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    public static bool Same(List<PartLine> a, List<PartLine> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (a[i].PartId != b[i].PartId || a[i].Amount != b[i].Amount || a[i].Unit != b[i].Unit)
                return false;
        return true;
    }
}

// Eine Menge als Bruch, damit erst am Ende gerundet wird. Was da ist, rundet nie auf null.
public readonly record struct Ratio(long Num, long Den)
{
    public static Ratio Of(long n) => new(n, 1);

    public Ratio Times(long n, long d) => Reduced(checked(Num * n), checked(Den * d));

    public Ratio Plus(Ratio o) => Reduced(checked(Num * o.Den + o.Num * Den), checked(Den * o.Den));

    public long Rounded => Num > 0 ? Math.Max((Num + Den / 2) / Den, 1) : 0;

    static Ratio Reduced(long n, long d)
    {
        var g = Math.Max(Gcd(Math.Abs(n), Math.Abs(d)), 1);
        return new(n / g, d / g);
    }

    static long Gcd(long a, long b)
    {
        while (b != 0) (a, b) = (b, a % b);
        return a;
    }
}

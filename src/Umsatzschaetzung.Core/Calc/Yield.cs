using System.Text;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Calc;

internal static class Yield
{
    readonly record struct Parts(long Shrinkage, long OwnUse, long Staff, long Free)
    {
        public long Sum => Shrinkage + OwnUse + Staff + Free;

        public string Formula(Unit unit)
        {
            var shrinkLabel = unit == Unit.Ml ? "Schankverlust" : "Schwund";
            var b = new StringBuilder("100 %");
            foreach (var (v, label) in new[] { (Shrinkage, shrinkLabel), (OwnUse, "Eigenverbrauch"), (Staff, "Personalverzehr"), (Free, "Freiabgabe") })
                if (v != 0) b.Append(" − ").Append(Format.Bp(v)).Append(' ').Append(label);
            return b.ToString();
        }
    }

    public static void Run(Case c, RuleSet rs, SortedDictionary<string, IngredientUse> uses)
    {
        foreach (var id in uses.Keys)
        {
            var u = uses[id];
            if (!rs.Ingredients.TryGetValue(id, out var ing))
                throw new InvalidOperationException($"yield: unknown ingredient \"{id}\"");
            var unit = Scale.Of(rs, id) ?? Unit.Piece;
            var parts = default(Parts);
            List<SourceRef> sources = [];
            if (Match.YieldRule(c, rs, ing) is var (r, chosen))
            {
                parts = new(r.Shrinkage, r.OwnUse, r.Staff, r.Free);
                sources.Add(new SourceRef { Kind = SourceKind.Rule, Entity = Entity.YieldRule, EntityId = r.Id });
                if (chosen)
                    sources.Add(new SourceRef { Kind = SourceKind.Case, EntityId = c.Id, Reason = "gewählte Ertragsregel „" + r.Name + "“" });
            }
            u.Sellable = u.Used * (Bp.Full - parts.Sum) / Bp.Full;
            u.Node = new Node
            {
                Label = ing.Name + ": verkaufsfähige Menge",
                Value = u.Sellable,
                Unit = Units.Value(unit),
                Formula = $"{Format.Qty(u.Used, unit)} × ({parts.Formula(unit)}) = {Format.Qty(u.Sellable, unit)}",
                Inputs = [u.Node!],
                Sources = sources,
            };
        }
    }
}

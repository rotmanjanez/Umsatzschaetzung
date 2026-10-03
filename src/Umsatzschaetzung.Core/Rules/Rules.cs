using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Rules;

public sealed class RulesException(string message, Exception? inner = null) : Exception(message, inner);

public static class RuleCheck
{
    public static void Validate(RuleSet rs)
    {
        foreach (var (id, e) in rs.Categories) ValidateCategory(Keyed(id, e));
        foreach (var (id, e) in rs.Mappings) ValidateMapping(rs, Keyed(id, e));
        foreach (var (id, e) in rs.Products) ValidateProduct(rs, Keyed(id, e));
        ValidateNesting(rs);
        foreach (var (id, e) in rs.YieldRules) ValidateYieldRule(rs, Keyed(id, e));
        HashSet<string> kennzahlen = [];
        foreach (var (id, e) in rs.Gewerbezweige)
            if (!kennzahlen.Add(ValidateGewerbezweig(Keyed(id, e)).Kennzahl))
                throw new RulesException($"Gewerbekennzahl {e.Kennzahl} gibt es schon");
        foreach (var (id, e) in rs.Templates) ValidateTemplate(rs, Keyed(id, e));
    }

    // A mapping refers to a product and nothing refers to it, so one added to a valid set
    // needs no more than its own check.
    public static void Validate(RuleSet rs, ArticleMapping mapping) => ValidateMapping(rs, Keyed(mapping.Id, mapping));

    // Names of the entries that would dangle if this entity were deleted.
    public static List<string> Users(RuleSet rs, Entity kind, string id) => kind switch
    {
        Entity.Category =>
        [
            .. Names(rs.Products.Values.Where(p => p.CategoryId == id).Select(p => "Produkt \u201e" + p.Name + "\u201c")),
            .. Names(rs.YieldRules.Values.Where(y => y.CategoryId == id).Select(y => "Ausbeuteregel \u201e" + y.Name + "\u201c")),
        ],
        Entity.Product =>
        [
            .. CountedIn(rs, id),
            .. Names(rs.YieldRules.Values.Where(y => y.ProductId == id).Select(y => "Ausbeuteregel \u201e" + y.Name + "\u201c")),
        ],
        _ => [],
    };

    // Names of the mappings and recipes that count in this product's unit.
    public static List<string> CountedIn(RuleSet rs, string id) =>
    [
        .. Names(Counting(rs, id).Where(c => c.Kind == Entity.Mapping).Select(c => c.Name)),
        .. Names(Counting(rs, id).Where(c => c.Kind == Entity.Product).Select(c => c.Name)),
    ];

    static IEnumerable<(Entity Kind, string Id, string Name)> Counting(RuleSet rs, string id) =>
        rs.Mappings.Values.Where(m => m.ProductId == id)
            .Select(m => (Entity.Mapping, m.Id, "Zuordnung \u201e" + (m.Name ?? m.SupplierArticleId ?? m.Gtin ?? m.Id) + "\u201c"))
            .Concat(rs.Products.Values.Where(p => p.Id != id && p.Recipe.Exists(l => l.PartId == id))
                .Select(p => (Entity.Product, p.Id, "Produkt \u201e" + p.Name + "\u201c")));

    // A product's unit is what its mappings' factors and its recipe lines count in: it changes only
    // together with each of them, else they would read their numbers in another unit.
    public static void Rescaled(RuleSet before, RuleSet after, IEnumerable<IRuleEntity> written)
    {
        var with = written.Select(e => e.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in written.OfType<Product>())
        {
            if (!before.Products.TryGetValue(p.Id, out var was) || was.Unit == p.Unit) continue;
            var left = Counting(after, p.Id).Where(c => !with.Contains(c.Id)).Select(c => c.Name).ToList();
            if (left.Count > 0)
                throw new RulesException($"Produkt \"{p.Name}\": die Einheit bleibt, solange es verwendet wird von {string.Join(", ", Names(left))}");
        }
    }

    static IEnumerable<string> Names(IEnumerable<string> names) => names.OrderBy(n => n, StringComparer.Ordinal);

    static T Keyed<T>(string id, T e) where T : IRuleEntity
    {
        if (id == "") throw new RulesException("leere Entitäts-ID");
        e.Id = id;
        e.Meta ??= new();
        if (e.Meta is { ValidFrom: { } from, ValidTo: { } to } && to <= from)
            throw new RulesException($"\"{id}\": „gültig bis“ muss nach „gültig ab“ liegen");
        return e;
    }

    static void ValidateCategory(Category e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new RulesException("Kategorie: Name darf nicht leer sein");
        foreach (var g in e.Gewerbe)
            if (!Gewerbe.Kennzahl(g)) throw new RulesException($"Kategorie \"{e.Name}\": Gewerbekennzahl \"{g}\" ist ungültig");
        foreach (var u in e.Gebinde)
            if (Units.Lookup(u) is not { Container: true } info || info.Code != u)
                throw new RulesException($"Kategorie \"{e.Name}\": \"{u}\" ist kein Packmittelcode");
    }

    static Gewerbezweig ValidateGewerbezweig(Gewerbezweig e)
    {
        if (!Gewerbe.Kennzahl(e.Kennzahl)) throw new RulesException($"Gewerbekennzahl \"{e.Kennzahl}\" ist ungültig");
        if (string.IsNullOrWhiteSpace(e.Name)) throw new RulesException($"Gewerbekennzahl {e.Kennzahl}: Bezeichnung darf nicht leer sein");
        return e;
    }

    static void ValidateMapping(RuleSet rs, ArticleMapping e)
    {
        if (string.IsNullOrWhiteSpace(e.SupplierArticleId) && string.IsNullOrWhiteSpace(e.Gtin) && ArticleName.Canonical(e.Name) == "")
            throw new RulesException("Zuordnung: Artikelnummer, GTIN oder Namensmuster erforderlich");
        if (e.Factor is <= 0) throw new RulesException("Zuordnung: Faktor muss größer als 0 sein");
        if (!rs.Products.ContainsKey(e.ProductId))
            throw new RulesException($"Zuordnung: Produkt \"{e.ProductId}\" existiert nicht");
    }

    static void ValidateProduct(RuleSet rs, Product e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new RulesException("Produkt: Name darf nicht leer sein");
        if (Scale.Of(e) is not { } unit)
            throw new RulesException($"Produkt \"{e.Name}\": \"{e.Unit}\" ist keine Einheit, in der es sich zählen lässt");
        if (e.Batch <= 0) throw new RulesException($"Produkt \"{e.Name}\": ein Ansatz muss mehr als 0 {Format.UnitName(unit)} ergeben");
        if (!string.IsNullOrEmpty(e.CategoryId) && !rs.Categories.ContainsKey(e.CategoryId))
            throw new RulesException($"Produkt \"{e.Name}\": Kategorie \"{e.CategoryId}\" existiert nicht");
        if (e.Piece is { } p && (p.Amount <= 0 || p.Unit == Unit.Piece))
            throw new RulesException($"Produkt \"{e.Name}\": das Stückgewicht muss größer als 0 sein und in g oder ml gelten");
        foreach (var l in e.Recipe)
        {
            if (!rs.Products.TryGetValue(l.PartId, out var part))
                throw new RulesException($"Produkt \"{e.Name}\": Bestandteil \"{l.PartId}\" existiert nicht");
            if (l.Amount <= 0)
                throw new RulesException($"Produkt \"{e.Name}\": Menge von \"{part.Name}\" muss größer als 0 sein");
            if (Units.Lookup(l.Unit) is not { Container: false } u)
                throw new RulesException($"Produkt \"{e.Name}\": \"{part.Name}\" hat die unbekannte Einheit \"{l.Unit}\"");
            if (Scale.Of(part) is { } partUnit && u.Base != partUnit)
                throw new RulesException($"Produkt \"{e.Name}\": \"{part.Name}\" zählt in {Format.UnitName(partUnit)}, nicht in {u.Name}");
        }
    }

    static void ValidateNesting(RuleSet rs)
    {
        HashSet<string> open = [], done = [];
        foreach (var id in rs.Products.Keys.OrderBy(k => k, StringComparer.Ordinal)) Descend(rs, id, open, done);
    }

    static void Descend(RuleSet rs, string id, HashSet<string> open, HashSet<string> done)
    {
        if (done.Contains(id) || !rs.Products.TryGetValue(id, out var p)) return;
        if (!open.Add(id)) throw new RulesException($"Produkt \"{p.Name}\": das Rezept enthält sich selbst");
        foreach (var l in p.Recipe) Descend(rs, l.PartId, open, done);
        open.Remove(id);
        done.Add(id);
    }

    static void ValidateYieldRule(RuleSet rs, YieldRule e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new RulesException("Ausbeuteregel: Name darf nicht leer sein");
        var category = e.CategoryId ?? "";
        var product = e.ProductId ?? "";
        if (category == "" && product == "")
            throw new RulesException("Ausbeuteregel: Kategorie oder Produkt erforderlich");
        if (e.Default && rs.YieldRules.Values.FirstOrDefault(o => o.Id != e.Id && o.Default
                && (o.ProductId ?? "") == product && (product != "" || (o.CategoryId ?? "") == category)) is { } other)
            throw new RulesException($"Ausbeuteregel \"{e.Name}\": Standard ist bereits \"{other.Name}\"");
        if (product != "" && !rs.Products.ContainsKey(product))
            throw new RulesException($"Ausbeuteregel: Produkt \"{product}\" existiert nicht");
        if (category != "" && !rs.Categories.ContainsKey(category))
            throw new RulesException($"Ausbeuteregel: Kategorie \"{category}\" existiert nicht");
        if (e.Deduction is < 0 or > Bp.Full)
            throw new RulesException("Ausbeuteregel: Abzug muss zwischen 0 und 100 % liegen");
    }

    static void ValidateTemplate(RuleSet rs, ReportTemplate e)
    {
        if (string.IsNullOrWhiteSpace(e.Name)) throw new RulesException("Vorlage: Name darf nicht leer sein");
        if (e.Default && rs.Templates.Values.FirstOrDefault(o => o.Id != e.Id && o.Default) is { } other)
            throw new RulesException($"Vorlage \"{e.Name}\": Standard ist bereits \"{other.Name}\"");
        try
        {
            Reports.Template.Check(e.Source);
        }
        catch (Reports.TemplateError x)
        {
            throw new RulesException($"Vorlage \"{e.Name}\": {x.Message}", x);
        }
    }
}

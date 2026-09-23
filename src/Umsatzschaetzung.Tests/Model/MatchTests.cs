using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class MatchTests
{
    const string Supplier = "Rheinland Getränke";

    static readonly DateOnly Day = new(2025, 3, 1);

    static RuleSet Rules(params ArticleMapping[] mappings)
    {
        var rs = new RuleSet();
        foreach (var m in mappings)
        {
            m.Confirmed = m.Confirmed || m.Observed is null;
            rs.Put(m);
        }
        return rs;
    }

    static InvoiceLine Line(string name = "Pils 0,5 l", string? article = "31090", string? gtin = "4006381333931", string unit = "XCS") =>
        new() { Name = name, SellerArticleId = article, Gtin = gtin, UnitCode = unit };

    [Fact]
    public void TheArticleNumberOfTheSupplierComesFirst()
    {
        var rs = Rules(
            new ArticleMapping { Id = "a.name", Name = "Pils 0,5 l", IngredientId = "ing.name" },
            new ArticleMapping { Id = "b.gtin", Gtin = "4006381333931", IngredientId = "ing.gtin" },
            new ArticleMapping { Id = "c.article", SupplierName = Supplier, SupplierArticleId = "31090", IngredientId = "ing.article" });
        Assert.Equal("c.article", Match.Mapping(rs, Supplier, Day, Line())?.Id);
        Assert.Equal("b.gtin", Match.Mapping(rs, "Anderer", Day, Line())?.Id);
        Assert.Equal("b.gtin", Match.Mapping(rs, null, Day, Line())?.Id);
        Assert.Equal("a.name", Match.Mapping(rs, Supplier, Day, Line(article: null, gtin: null))?.Id);
    }

    [Fact]
    public void ANameMatchIgnoresCaseAndPunctuation()
    {
        var rs = Rules(new ArticleMapping { Id = "m", Name = "PILS 0,5L", IngredientId = "i" });
        Assert.Null(Match.Mapping(rs, Supplier, Day, Line("pils 0,5 l", null, null)));
        Assert.Equal("m", Match.Mapping(rs, Supplier, Day, Line("pils  0,5l!", null, null))?.Id);
    }

    [Fact]
    public void AnEmptyNameMatchesNothing()
    {
        var rs = Rules(new ArticleMapping { Id = "m", Name = "--", IngredientId = "i" });
        Assert.Null(Match.Mapping(rs, Supplier, Day, Line("", null, null)));
    }

    [Fact]
    public void TheMappingALineCarriesWinsEvenWhenNothingElseFits()
    {
        var rs = Rules(
            new ArticleMapping { Id = "a", Gtin = "4006381333931", IngredientId = "i" },
            new ArticleMapping { Id = "b", Name = "Weizen", IngredientId = "j", Meta = new() { ValidTo = new DateOnly(2000, 1, 1) } });
        var line = Line();
        line.MappingId = "b";
        Assert.Equal("b", Match.Mapping(rs, Supplier, Day, line)?.Id);
        line.MappingId = "weg";
        Assert.Equal("a", Match.Mapping(rs, Supplier, Day, line)?.Id);
    }

    [Fact]
    public void AnotherUnitOrAnExpiredRuleDoesNotApply()
    {
        var rs = Rules(
            new ArticleMapping { Id = "a", Gtin = "4006381333931", UnitCode = "XBO", IngredientId = "i" },
            new ArticleMapping { Id = "b", Gtin = "4006381333931", IngredientId = "i", Meta = new() { ValidFrom = new DateOnly(2025, 4, 1) } });
        Assert.Null(Match.Mapping(rs, Supplier, Day, Line()));
        Assert.Equal("a", Match.Mapping(rs, Supplier, Day, Line(unit: "xbo"))?.Id);
        Assert.Equal("a", Match.Mapping(rs, Supplier, Day, Line(unit: ""))?.Id);
        Assert.Equal("b", Match.Mapping(rs, Supplier, new DateOnly(2025, 4, 1), Line())?.Id);
        Assert.Equal("a", Match.Mapping(rs, Supplier, null, Line(unit: "XBO"))?.Id);
    }

    [Fact]
    public void AnUnconfirmedGuessFitsItsKeyWhateverTheWording()
    {
        var rs = Rules(new ArticleMapping { Id = "m", Gtin = "4006381333931", Observed = "Pils 0,5 l", IngredientId = "i" });
        Assert.Equal("m", Match.Mapping(rs, Supplier, Day, Line("Pils O,5 1"))?.Id);
        Assert.True(Match.Fits(rs.Mappings["m"], Supplier, Day, Line("Pils O,5 1")));
    }

    [Fact]
    public void AmongEqualRulesTheSmallestIdWins()
    {
        var rs = Rules(
            new ArticleMapping { Id = "m.b", Gtin = "4006381333931", IngredientId = "i.b" },
            new ArticleMapping { Id = "m.a", Gtin = "4006381333931", IngredientId = "i.a" });
        Assert.Equal("m.a", Match.Mapping(rs, Supplier, Day, Line())?.Id);
    }

    [Fact]
    public void FitsTellsWhetherALineStillBelongsToItsRule()
    {
        var m = new ArticleMapping { Id = "m", SupplierName = Supplier, SupplierArticleId = "31090", UnitCode = "XCS", Confirmed = true };
        Assert.True(Match.Fits(m, Supplier, Day, Line()));
        Assert.False(Match.Fits(m, Supplier, Day, Line(article: "31091", gtin: null)));
        Assert.False(Match.Fits(m, Supplier, Day, Line(unit: "XBO")));
        Assert.False(Match.Fits(m, "Anderer", Day, Line(gtin: null)));
    }

    static Case Kase(params YieldChoice[] yields) => new() { PeriodTo = new DateOnly(2025, 12, 31), Yields = [.. yields] };

    static (RuleSet Rules, Ingredient Ingredient) Yields()
    {
        var rs = new RuleSet();
        var ing = new Ingredient { Id = "ing.bier", CategoryId = "cat.bier" };
        rs.Put(ing);
        rs.Put(new YieldRule { Id = "y.cat", CategoryId = "cat.bier" });
        rs.Put(new YieldRule { Id = "y.cat.default", CategoryId = "cat.bier", Default = true });
        rs.Put(new YieldRule { Id = "y.ing.b", IngredientId = "ing.bier" });
        rs.Put(new YieldRule { Id = "y.ing.a", IngredientId = "ing.bier" });
        rs.Put(new YieldRule { Id = "y.old", IngredientId = "ing.bier", Default = true, Meta = new() { ValidTo = new DateOnly(2025, 1, 1) } });
        rs.Put(new YieldRule { Id = "y.other", CategoryId = "cat.wein", Default = true });
        return (rs, ing);
    }

    [Fact]
    public void TheCaseChoiceForTheIngredientComesFirst()
    {
        var (rs, ing) = Yields();
        var c = Kase(new() { CategoryId = "cat.bier", YieldRuleId = "y.cat" }, new() { IngredientId = "ing.bier", YieldRuleId = "y.cat.default" });
        Assert.Equal(("y.cat.default", true), Match.YieldRule(c, rs, ing) is { } r ? (r.Rule.Id, r.Chosen) : default);
    }

    [Fact]
    public void ACaseChoiceForTheCategoryComesNext()
    {
        var (rs, ing) = Yields();
        var c = Kase(new() { CategoryId = "cat.bier", YieldRuleId = "y.cat" }, new() { IngredientId = "ing.bier", YieldRuleId = "weg" });
        Assert.Equal(("y.cat", true), Match.YieldRule(c, rs, ing) is { } r ? (r.Rule.Id, r.Chosen) : default);
    }

    [Fact]
    public void WithoutAChoiceTheIngredientsFirstValidRuleApplies()
    {
        var (rs, ing) = Yields();
        Assert.Equal(("y.ing.a", false), Match.YieldRule(Kase(), rs, ing) is { } r ? (r.Rule.Id, r.Chosen) : default);
    }

    [Fact]
    public void ThenTheCategoryDefault()
    {
        var (rs, ing) = Yields();
        rs.YieldRules.Remove("y.ing.a");
        rs.YieldRules.Remove("y.ing.b");
        Assert.Equal("y.cat.default", Match.YieldRule(Kase(), rs, ing)?.Rule.Id);
        rs.YieldRules.Remove("y.cat.default");
        rs.YieldRules.Remove("y.cat");
        Assert.Null(Match.YieldRule(Kase(), rs, ing));
    }
}

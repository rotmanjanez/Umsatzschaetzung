using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

// The ranking rules on chosen cosines: the cache knows every text, so no model runs.
public class MatcherRankingTests
{
    const string Line = "Ware";
    const string Supplier = "Rheinland Getränke Fachgroßhandel GmbH";

    static int Confidence(double cos)
    {
        using var e = new Encoder();
        return e.Confidence((float)cos);
    }

    static Ingredient Ing(string id, string name, string category = "") => new() { Id = id, Name = name, CategoryId = category };

    static RuleSet Rules(params IRuleEntity[] entities)
    {
        var rs = new RuleSet { Version = 1 };
        foreach (var e in entities) rs.Put(e);
        return rs;
    }

    static List<Suggestion> Suggest(FixedCache cache, RuleSet rs, string gewerbe = "", string? supplier = null, InvoiceLine? line = null)
    {
        using var m = new Matcher(cache.Query(line?.Name ?? Line));
        return m.Suggest(rs, gewerbe, supplier, line ?? new InvoiceLine { Name = Line });
    }

    static List<string> Ids(List<Suggestion> s) => [.. s.Select(x => x.Mapping.IngredientId)];

    [Fact]
    public void ConfidenceIsTheCalibratedCosine()
    {
        var s = Suggest(new FixedCache().At("A", 0.8), Rules(Ing("ing.a", "A")));
        var only = Assert.Single(s);
        Assert.Equal(("ing.a", Confidence(0.8), OriginKind.Encoder), (only.Mapping.IngredientId, only.Confidence, only.Kind));
    }

    [Fact]
    public void ACandidateBelowTheFloorIsDropped()
    {
        Assert.InRange(Confidence(0.1), 0, 19);
        var s = Suggest(new FixedCache().At("A", 0.9).At("B", 0.1), Rules(Ing("ing.a", "A"), Ing("ing.b", "B")));
        Assert.Equal(["ing.a"], Ids(s));
    }

    [Fact]
    public void AtMostFiveCandidatesComeBackBestFirst()
    {
        var cache = new FixedCache();
        var rs = Rules();
        for (var i = 0; i < 8; i++)
        {
            cache.At("W" + i, 0.60 + i * 0.05);
            rs.Put(Ing("ing." + i, "W" + i));
        }
        var s = Suggest(cache, rs);
        Assert.Equal(["ing.7", "ing.6", "ing.5", "ing.4", "ing.3"], Ids(s));
        Assert.Equal(s.Select(x => x.Confidence).OrderDescending(), s.Select(x => x.Confidence));
    }

    [Fact]
    public void ATieIsBrokenByTheIngredientIdAndStaysPut()
    {
        var cache = new FixedCache().At("Zweite", 0.8).At("Erste", 0.8).Query(Line);
        var rs = Rules(Ing("ing.z", "Erste"), Ing("ing.a", "Zweite"), Ing("ing.m", "Erste"));
        using var m = new Matcher(cache);
        var first = m.Suggest(rs, "", null, new InvoiceLine { Name = Line });
        var again = m.Suggest(rs, "", null, new InvoiceLine { Name = Line });
        Assert.Equal(["ing.a", "ing.m", "ing.z"], Ids(first));
        Assert.Equal(Ids(first), Ids(again));
        Assert.All(first, x => Assert.Equal(first[0].Confidence, x.Confidence));
    }

    [Fact]
    public void AnIngredientScoresByItsClosestWordingNotTheirAverage()
    {
        var x = Ing("ing.x", "Fern");
        x.Aliases = ["Nah", "Mittel"];
        var cache = new FixedCache().At("Fern", 0.1).At("Nah", 0.95).At("Mittel", 0.5).At("Y", 0.9);
        var s = Suggest(cache, Rules(x, Ing("ing.y", "Y")));
        Assert.Equal(["ing.x", "ing.y"], Ids(s));
        Assert.Equal(Confidence(0.95), s[0].Confidence);
    }

    [Theory]
    [InlineData("56101.0", true)]
    [InlineData("561", true)]
    [InlineData("47250.0", true)]
    [InlineData("96021.0", false)]
    [InlineData("", true)]
    public void OnlyTheIngredientsOfTheCaseGewerbeAreOffered(string gewerbe, bool korn)
    {
        var rs = Rules(
            new Category { Id = "cat.spirituosen", Name = "Spirituosen", Gewerbe = ["561", "47250.0"] },
            new Category { Id = "cat.bier", Name = "Bier" },
            Ing("ing.korn", "Korn", "cat.spirituosen"),
            Ing("ing.bier", "Bier", "cat.bier"),
            Ing("ing.lose", "Lose", "cat.fehlt"));
        var cache = new FixedCache().At("Korn", 0.9).At("Bier", 0.8).At("Lose", 0.7);
        var s = Suggest(cache, rs, gewerbe);
        Assert.Equal(korn ? ["ing.korn", "ing.bier", "ing.lose"] : ["ing.bier", "ing.lose"], Ids(s));
    }

    [Fact]
    public void AnotherGewerbeOnTheSameRuleSetReindexes()
    {
        var rs = Rules(
            new Category { Id = "cat.spirituosen", Name = "Spirituosen", Gewerbe = ["561"] },
            Ing("ing.korn", "Korn", "cat.spirituosen"),
            Ing("ing.bier", "Bier"));
        using var m = new Matcher(new FixedCache().At("Korn", 0.9).At("Bier", 0.8).Query(Line));
        var line = new InvoiceLine { Name = Line };
        Assert.Equal(["ing.korn", "ing.bier"], Ids(m.Suggest(rs, "56101.0", null, line)));
        Assert.Equal(["ing.bier"], Ids(m.Suggest(rs, "96021.0", null, line)));
        Assert.Equal(["ing.korn", "ing.bier"], Ids(m.Suggest(rs, "56101.0", null, line)));
    }

    [Fact]
    public void AnIngredientNoLongerValidIsNotOffered()
    {
        var old = Ing("ing.alt", "Alt");
        old.Meta.ValidTo = new DateOnly(2020, 1, 1);
        var later = Ing("ing.neu", "Neu");
        later.Meta.ValidFrom = DateOnly.FromDateTime(DateTime.Now).AddYears(1);
        var s = Suggest(new FixedCache().At("Alt", 0.9).At("Neu", 0.9).At("B", 0.5), Rules(old, later, Ing("ing.b", "B")));
        Assert.Equal(["ing.b"], Ids(s));
    }

    [Fact]
    public void AnIngredientIsOfferedOnTheDatesItWasValid()
    {
        var old = Ing("ing.alt", "Alt");
        old.Meta.ValidTo = new DateOnly(2020, 1, 1);
        var rs = Rules(old, Ing("ing.b", "B"));
        using var m = new Matcher(new FixedCache().At("Alt", 0.9).At("B", 0.5).Query(Line));
        var line = new InvoiceLine { Name = Line };

        Assert.Equal(["ing.alt", "ing.b"], Ids(m.Suggest(rs, "", null, line, new DateOnly(2019, 6, 1))));
        Assert.Equal(["ing.b"], Ids(m.Suggest(rs, "", null, line, new DateOnly(2021, 6, 1))));
    }

    [Fact]
    public void AConfirmedMappingTeachesItsWordingAndAGuessDoesNot()
    {
        var rs = Rules(
            Ing("ing.a", "A"),
            Ing("ing.b", "B"),
            new ArticleMapping { Id = "map.a", Observed = "Gelernt", IngredientId = "ing.a", Confirmed = true },
            new ArticleMapping { Id = "map.b", Observed = "Geraten", IngredientId = "ing.b", Confirmed = false });
        var s = Suggest(new FixedCache().At("A", 0.1).At("B", 0.1).At("Gelernt", 0.97).At("Geraten", 0.99), rs);
        var only = Assert.Single(s);
        Assert.Equal(("ing.a", Confidence(0.97), OriginKind.Encoder, ""), (only.Mapping.IngredientId, only.Confidence, only.Kind, only.Mapping.Id));
    }

    [Fact]
    public void AConfirmedMappingCannotBringBackAnIngredientOutsideTheGewerbe()
    {
        var rs = Rules(
            new Category { Id = "cat.spirituosen", Name = "Spirituosen", Gewerbe = ["561"] },
            Ing("ing.korn", "Korn", "cat.spirituosen"),
            new ArticleMapping { Id = "map.korn", Name = "Klarer", IngredientId = "ing.korn", Confirmed = true });
        Assert.Empty(Suggest(new FixedCache().At("Korn", 0.9).At("Klarer", 0.99), rs, "96021.0"));
    }

    [Fact]
    public void AnUpperCaseLineIsLookedUpInTitleCase()
    {
        var cache = new FixedCache().At("Fassbier", 0.9).Query("Fassbier Pils, Keg 50 L");
        using var m = new Matcher(cache);
        var s = m.Suggest(Rules(Ing("ing.fass", "Fassbier")), "", null, new InvoiceLine { Name = "FASSBIER PILS, KEG 50 L" });
        Assert.Equal(["ing.fass"], Ids(s));
        Assert.Equal("FASSBIER PILS, KEG 50 L", s[0].Mapping.Observed);
    }

    [Fact]
    public void AnEmptyRuleSetSuggestsNothing() => Assert.Empty(Suggest(new FixedCache(), Rules()));

    [Fact]
    public void AnExactHitLeadsWithFullConfidenceAndItsIngredientIsNotRepeated()
    {
        var rs = Rules(
            Ing("ing.a", "A"),
            Ing("ing.b", "B"),
            new ArticleMapping { Id = "map.a", Name = Line, IngredientId = "ing.a", Factor = 42, Confirmed = true });
        var s = Suggest(new FixedCache().At("A", 0.99).At("B", 0.8), rs);
        Assert.Equal(["ing.a", "ing.b"], Ids(s));
        Assert.Equal((OriginKind.Exact, 100), (s[0].Kind, s[0].Confidence));
        Assert.Same(rs.Mappings["map.a"], s[0].Mapping);
        Assert.Equal(OriginKind.Encoder, s[1].Kind);
    }

    [Fact]
    public void AnExactHitStandsAloneWhenTheEncoderHasNothing()
    {
        var rs = Rules(Ing("ing.a", "A"), new ArticleMapping { Id = "map.a", Name = Line, IngredientId = "ing.a", Confirmed = true });
        var only = Assert.Single(Suggest(new FixedCache().At("A", 0.1), rs));
        Assert.Equal(("map.a", OriginKind.Exact), (only.Mapping.Id, only.Kind));
    }

    [Theory]
    [InlineData(Supplier, "A-1", null, Supplier, "A-1", null, null)]
    [InlineData(null, "A-1", null, null, null, null, Line)]
    [InlineData(Supplier, null, null, Supplier, null, null, Line)]
    [InlineData(null, null, "4001234567890", null, null, "4001234567890", null)]
    [InlineData(Supplier, "A-1", "4001234567890", Supplier, "A-1", "4001234567890", null)]
    public void AProposalIdentifiesTheArticleByWhatTheLineOffers(string? supplier, string? articleId, string? gtin,
        string? wantSupplier, string? wantArticle, string? wantGtin, string? wantName)
    {
        var line = new InvoiceLine { Name = Line, SellerArticleId = articleId, Gtin = gtin };
        var m = Assert.Single(Suggest(new FixedCache().At("A", 0.9), Rules(Ing("ing.a", "A")), supplier: supplier, line: line)).Mapping;
        Assert.Equal(("", wantSupplier, wantArticle, wantGtin, wantName, Line, false),
            (m.Id, m.SupplierName, m.SupplierArticleId, m.Gtin, m.Name, m.Observed, m.Confirmed));
    }

    // The factor is only true for the unit it was read against: a bottle's 700 ml applied
    // to a line billed in crates would count a twentieth of the goods.
    [Theory]
    [InlineData("XCS", "XCS")]
    [InlineData("", null)]
    public void AProposalIsBoundToTheUnitItsFactorWasReadFor(string unitCode, string? bound)
    {
        var line = new InvoiceLine { Name = "Pils Kiste 20 x 0,5 l", UnitCode = unitCode };
        var m = Assert.Single(Suggest(new FixedCache().At("A", 0.9), Rules(Ing("ing.a", "A")), line: line)).Mapping;
        Assert.Equal(bound, m.UnitCode);
        Assert.True(Match.Fits(m, null, null, line));
        Assert.Equal(bound is null, Match.Fits(m, null, null, new InvoiceLine { Name = line.Name, UnitCode = "XBO" }));
    }

    [Theory]
    [InlineData("MLT", 10000L)]
    [InlineData("CLT", 10000L)]
    [InlineData("GRM", null)]
    [InlineData("H87", null)]
    [InlineData(null, null)]
    public void TheFactorComesFromThePackSizeInTheRecipeUnit(string? recipeUnit, long? factor)
    {
        const string name = "Pils Kiste 20 x 0,5 l";
        var rs = Rules(Ing("ing.a", "A"));
        if (recipeUnit is not null)
            rs.Put(new Product { Id = "prod.a", Name = "A", Recipe = [new() { IngredientId = "ing.a", Amount = 1, Unit = recipeUnit }] });
        var line = new InvoiceLine { Name = name, UnitCode = "XCS" };
        var s = Suggest(new FixedCache().At("A", 0.9), rs, line: line);
        Assert.Equal(factor, Assert.Single(s).Mapping.Factor);
    }
}

using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

// The ranking rules on chosen cosines: the cache knows every text, so no model runs.
public class MatcherRankingTests
{
    const string Line = "Ware";
    const string Supplier = "Rheinland Getränke Fachgroßhandel GmbH";

    static async Task<int> Confidence(double cos)
    {
        await Encoders.Shipped.Load();
        return Encoders.Shipped.Confidence((float)cos);
    }

    const string Goods = "cat.ware";

    static Product Ware(string id, string name, string? category = Goods) => new() { Id = id, Name = name, Unit = "H87", CategoryId = category };

    static RuleSet Rules(params IRuleEntity[] entities)
    {
        var rs = new RuleSet { Version = 1 };
        rs.Put(new Category { Id = Goods, Name = "Ware" });
        foreach (var e in entities) rs.Put(e);
        return rs;
    }

    static Task<List<Suggestion>> Suggest(FixedCache cache, RuleSet rs, string gewerbe = "", string? supplier = null, InvoiceLine? line = null)
    {
        var m = new Matcher(Encoders.Shipped, cache.Query(line?.Name ?? Line));
        return m.Suggest(rs, gewerbe, supplier, line ?? new InvoiceLine { Name = Line });
    }

    static List<string> Ids(List<Suggestion> s) => [.. s.Select(x => x.Mapping.ProductId)];

    [Fact]
    public async Task ConfidenceIsTheCalibratedCosine()
    {
        var s = await Suggest(new FixedCache().At("A", 0.8), Rules(Ware("prod.a", "A")));
        var only = Assert.Single(s);
        Assert.Equal(("prod.a", await Confidence(0.8), OriginKind.Encoder), (only.Mapping.ProductId, only.Confidence, only.Kind));
    }

    [Fact]
    public async Task ACandidateBelowTheFloorIsDropped()
    {
        Assert.InRange(await Confidence(0.1), 0, 19);
        var s = await Suggest(new FixedCache().At("A", 0.9).At("B", 0.1), Rules(Ware("prod.a", "A"), Ware("prod.b", "B")));
        Assert.Equal(["prod.a"], Ids(s));
    }

    [Fact]
    public async Task AtMostFiveCandidatesComeBackBestFirst()
    {
        var cache = new FixedCache();
        var rs = Rules();
        for (var i = 0; i < 8; i++)
        {
            cache.At("W" + i, 0.60 + i * 0.05);
            rs.Put(Ware("prod." + i, "W" + i));
        }
        var s = await Suggest(cache, rs);
        Assert.Equal(["prod.7", "prod.6", "prod.5", "prod.4", "prod.3"], Ids(s));
        Assert.Equal(s.Select(x => x.Confidence).OrderDescending(), s.Select(x => x.Confidence));
    }

    [Fact]
    public async Task ATieIsBrokenByTheProductIdAndStaysPut()
    {
        var cache = new FixedCache().At("Zweite", 0.8).At("Erste", 0.8).Query(Line);
        var rs = Rules(Ware("prod.z", "Erste"), Ware("prod.a", "Zweite"), Ware("prod.m", "Erste"));
        var m = new Matcher(Encoders.Shipped, cache);
        var first = await m.Suggest(rs, "", null, new InvoiceLine { Name = Line }, ct: TestContext.Current.CancellationToken);
        var again = await m.Suggest(rs, "", null, new InvoiceLine { Name = Line }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(["prod.a", "prod.m", "prod.z"], Ids(first));
        Assert.Equal(Ids(first), Ids(again));
        Assert.All(first, x => Assert.Equal(first[0].Confidence, x.Confidence));
    }

    [Fact]
    public async Task AWareScoresByItsClosestWordingNotTheirAverage()
    {
        var x = Ware("prod.x", "Fern");
        x.Aliases = ["Nah", "Mittel"];
        var cache = new FixedCache().At("Fern", 0.1).At("Nah", 0.95).At("Mittel", 0.5).At("Y", 0.9);
        var s = await Suggest(cache, Rules(x, Ware("prod.y", "Y")));
        Assert.Equal(["prod.x", "prod.y"], Ids(s));
        Assert.Equal(await Confidence(0.95), s[0].Confidence);
    }

    [Theory]
    [InlineData("56101.0", true)]
    [InlineData("561", true)]
    [InlineData("47250.0", true)]
    [InlineData("96021.0", false)]
    [InlineData("", true)]
    public async Task OnlyTheWaresOfTheCaseGewerbeAreOffered(string gewerbe, bool korn)
    {
        var rs = Rules(
            new Category { Id = "cat.spirituosen", Name = "Spirituosen", Gewerbe = ["561", "47250.0"] },
            new Category { Id = "cat.bier", Name = "Bier" },
            Ware("prod.korn", "Korn", "cat.spirituosen"),
            Ware("prod.bier", "Bier", "cat.bier"),
            Ware("prod.lose", "Lose", "cat.fehlt"));
        var cache = new FixedCache().At("Korn", 0.9).At("Bier", 0.8).At("Lose", 0.7);
        var s = await Suggest(cache, rs, gewerbe);
        Assert.Equal(korn ? ["prod.korn", "prod.bier", "prod.lose"] : ["prod.bier", "prod.lose"], Ids(s));
    }

    [Fact]
    public async Task AnotherGewerbeOnTheSameRuleSetReindexes()
    {
        var rs = Rules(
            new Category { Id = "cat.spirituosen", Name = "Spirituosen", Gewerbe = ["561"] },
            Ware("prod.korn", "Korn", "cat.spirituosen"),
            Ware("prod.bier", "Bier"));
        var m = new Matcher(Encoders.Shipped, new FixedCache().At("Korn", 0.9).At("Bier", 0.8).Query(Line));
        var line = new InvoiceLine { Name = Line };
        Assert.Equal(["prod.korn", "prod.bier"], Ids(await m.Suggest(rs, "56101.0", null, line, ct: TestContext.Current.CancellationToken)));
        Assert.Equal(["prod.bier"], Ids(await m.Suggest(rs, "96021.0", null, line, ct: TestContext.Current.CancellationToken)));
        Assert.Equal(["prod.korn", "prod.bier"], Ids(await m.Suggest(rs, "56101.0", null, line, ct: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AWareNoLongerValidIsNotOffered()
    {
        var old = Ware("prod.alt", "Alt");
        old.Meta.ValidTo = new DateOnly(2020, 1, 1);
        var later = Ware("prod.neu", "Neu");
        later.Meta.ValidFrom = DateOnly.FromDateTime(DateTime.Now).AddYears(1);
        var s = await Suggest(new FixedCache().At("Alt", 0.9).At("Neu", 0.9).At("B", 0.5), Rules(old, later, Ware("prod.b", "B")));
        Assert.Equal(["prod.b"], Ids(s));
    }

    [Fact]
    public async Task AWareIsOfferedOnTheDatesItWasValid()
    {
        var old = Ware("prod.alt", "Alt");
        old.Meta.ValidTo = new DateOnly(2020, 1, 1);
        var rs = Rules(old, Ware("prod.b", "B"));
        var m = new Matcher(Encoders.Shipped, new FixedCache().At("Alt", 0.9).At("B", 0.5).Query(Line));
        var line = new InvoiceLine { Name = Line };

        Assert.Equal(["prod.alt", "prod.b"], Ids(await m.Suggest(rs, "", null, line, new DateOnly(2019, 6, 1), ct: TestContext.Current.CancellationToken)));
        Assert.Equal(["prod.b"], Ids(await m.Suggest(rs, "", null, line, new DateOnly(2021, 6, 1), ct: TestContext.Current.CancellationToken)));
    }

    [Fact]
    public async Task AConfirmedMappingTeachesItsWordingAndAGuessDoesNot()
    {
        var rs = Rules(
            Ware("prod.a", "A"),
            Ware("prod.b", "B"),
            new ArticleMapping { Id = "map.a", Observed = "Gelernt", ProductId = "prod.a", Confirmed = true },
            new ArticleMapping { Id = "map.b", Observed = "Geraten", ProductId = "prod.b", Confirmed = false });
        var s = await Suggest(new FixedCache().At("A", 0.1).At("B", 0.1).At("Gelernt", 0.97).At("Geraten", 0.99), rs);
        var only = Assert.Single(s);
        Assert.Equal(("prod.a", await Confidence(0.97), OriginKind.Encoder, ""), (only.Mapping.ProductId, only.Confidence, only.Kind, only.Mapping.Id));
    }

    [Fact]
    public async Task AConfirmedMappingCannotBringBackAWareOutsideTheGewerbe()
    {
        var rs = Rules(
            new Category { Id = "cat.spirituosen", Name = "Spirituosen", Gewerbe = ["561"] },
            Ware("prod.korn", "Korn", "cat.spirituosen"),
            new ArticleMapping { Id = "map.korn", Name = "Klarer", ProductId = "prod.korn", Confirmed = true });
        Assert.Empty(await Suggest(new FixedCache().At("Korn", 0.9).At("Klarer", 0.99), rs, "96021.0"));
    }

    [Fact]
    public async Task AnUpperCaseLineIsLookedUpInTitleCase()
    {
        var cache = new FixedCache().At("Fassbier", 0.9).Query("Fassbier Pils, Keg 50 L");
        var m = new Matcher(Encoders.Shipped, cache);
        var s = await m.Suggest(Rules(Ware("prod.fass", "Fassbier")), "", null, new InvoiceLine { Name = "FASSBIER PILS, KEG 50 L" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(["prod.fass"], Ids(s));
        Assert.Equal("FASSBIER PILS, KEG 50 L", s[0].Mapping.Observed);
    }

    [Fact]
    public async Task AnEmptyRuleSetSuggestsNothing() => Assert.Empty(await Suggest(new FixedCache(), Rules()));

    [Fact]
    public async Task AnExactHitLeadsWithFullConfidenceAndItsWareIsNotRepeated()
    {
        var rs = Rules(
            Ware("prod.a", "A"),
            Ware("prod.b", "B"),
            new ArticleMapping { Id = "map.a", Name = Line, ProductId = "prod.a", Factor = 42, Confirmed = true });
        var s = await Suggest(new FixedCache().At("A", 0.99).At("B", 0.8), rs);
        Assert.Equal(["prod.a", "prod.b"], Ids(s));
        Assert.Equal((OriginKind.Exact, 100), (s[0].Kind, s[0].Confidence));
        Assert.Same(rs.Mappings["map.a"], s[0].Mapping);
        Assert.Equal(OriginKind.Encoder, s[1].Kind);
    }

    [Fact]
    public async Task AnExactHitStandsAloneWhenTheEncoderHasNothing()
    {
        var rs = Rules(Ware("prod.a", "A"), new ArticleMapping { Id = "map.a", Name = Line, ProductId = "prod.a", Confirmed = true });
        var only = Assert.Single(await Suggest(new FixedCache().At("A", 0.1), rs));
        Assert.Equal(("map.a", OriginKind.Exact), (only.Mapping.Id, only.Kind));
    }

    [Theory]
    [InlineData(Supplier, "A-1", null, Supplier, "A-1", null, null)]
    [InlineData(null, "A-1", null, null, null, null, Line)]
    [InlineData(Supplier, null, null, Supplier, null, null, Line)]
    [InlineData(null, null, "4001234567890", null, null, "4001234567890", null)]
    [InlineData(Supplier, "A-1", "4001234567890", Supplier, "A-1", "4001234567890", null)]
    public async Task AProposalIdentifiesTheArticleByWhatTheLineOffers(string? supplier, string? articleId, string? gtin,
        string? wantSupplier, string? wantArticle, string? wantGtin, string? wantName)
    {
        var line = new InvoiceLine { Name = Line, SellerArticleId = articleId, Gtin = gtin };
        var m = Assert.Single(await Suggest(new FixedCache().At("A", 0.9), Rules(Ware("prod.a", "A")), supplier: supplier, line: line)).Mapping;
        Assert.Equal(("", wantSupplier, wantArticle, wantGtin, wantName, Line, false),
            (m.Id, m.SupplierName, m.SupplierArticleId, m.Gtin, m.Name, m.Observed, m.Confirmed));
    }

    // The factor is only true for the unit it was read against: a bottle's 700 ml applied
    // to a line billed in crates would count a twentieth of the goods.
    [Theory]
    [InlineData("XCS", "XCS")]
    [InlineData("", null)]
    public async Task AProposalIsBoundToTheUnitItsFactorWasReadFor(string unitCode, string? bound)
    {
        var line = new InvoiceLine { Name = "Pils Kiste 20 x 0,5 l", UnitCode = unitCode };
        var m = Assert.Single(await Suggest(new FixedCache().At("A", 0.9), Rules(Ware("prod.a", "A")), line: line)).Mapping;
        Assert.Equal(bound, m.UnitCode);
        Assert.True(Match.Fits(m, null, null, line));
        Assert.Equal(bound is null, Match.Fits(m, null, null, new InvoiceLine { Name = line.Name, UnitCode = "XBO" }));
    }

    [Theory]
    [InlineData("MLT", 10000L)]
    [InlineData("CLT", 10000L)]
    [InlineData("GRM", null)]
    [InlineData("H87", null)]
    public async Task TheFactorComesFromThePackSizeInTheProductsUnit(string unit, long? factor)
    {
        const string name = "Pils Kiste 20 x 0,5 l";
        var rs = Rules(new Product { Id = "prod.a", Name = "A", Unit = unit, CategoryId = "cat.bier" });
        var line = new InvoiceLine { Name = name, UnitCode = "XCS" };
        var s = await Suggest(new FixedCache().At("A", 0.9), rs, line: line);
        Assert.Equal(factor, Assert.Single(s).Mapping.Factor);
    }

    [Fact]
    public async Task APackSizeGivesNoFactorForWhatIsInNoSparte()
    {
        var rs = Rules(new Category { Id = "cat.kein", Name = "Kein Wareneinsatz" }, Ware("prod.verpackung", "Verpackung", "cat.kein"));
        var line = new InvoiceLine { Name = "Servietten 1000 St.", UnitCode = "XCT" };
        Assert.Null(Assert.Single(await Suggest(new FixedCache().At("Verpackung", 0.9), rs, line: line)).Mapping.Factor);
    }

    static RuleSet Beer() => Rules(
        new Category { Id = "cat.fass", Name = "Bier vom Fass", Gebinde = ["XKG", "XBA"] },
        new Category { Id = "cat.flasche", Name = "Bier Flasche", Gebinde = ["XBO", "XCS"] },
        Ware("prod.fass", "Fassbier", "cat.fass"),
        Ware("prod.flasche", "Flaschenbier", "cat.flasche"));

    [Theory]
    [InlineData("Pils 30 l", "XKG", "prod.fass")]
    [InlineData("Pils Fass 30 l", "H87", "prod.fass")]
    [InlineData("Pils 30 l KEG", "", "prod.fass")]
    [InlineData("Pils 20 x 0,5 l", "XCS", "prod.flasche")]
    public async Task ThePackagingDecidesBetweenWaresTheWordsCannotTellApart(string name, string unit, string want)
    {
        var cache = new FixedCache().At("Fassbier", 0.9).At("Flaschenbier", 0.9).Query(name);
        var m = new Matcher(Encoders.Shipped, cache);
        var s = await m.Suggest(Beer(), "", null, new InvoiceLine { Name = name, UnitCode = unit }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(want, s[0].Mapping.ProductId);
        Assert.Equal(await Confidence(0.9), s[0].Confidence);
    }

    [Fact]
    public async Task WithoutAContainerThePackagingSaysNothing()
    {
        var cache = new FixedCache().At("Fassbier", 0.8).At("Flaschenbier", 0.9).Query("Pils 0,33 l");
        var m = new Matcher(Encoders.Shipped, cache);
        var s = await m.Suggest(Beer(), "", null, new InvoiceLine { Name = "Pils 0,33 l", UnitCode = "H87" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal([await Confidence(0.9), await Confidence(0.8)], s.Select(x => x.Confidence));
    }

    // A ware is what has a category, an alias or a confirmed mapping; a dish with none of them only
    // reads like what it is made of and stays out of the ranking.
    [Fact]
    public async Task AProductIsRankedByItsCategoryAnAliasOrAConfirmedMapping()
    {
        var rs = Beer();
        rs.Put(new Product { Id = "prod.krug", Name = "Bierkrug", Unit = "H87", Aliases = ["Krug"] });
        rs.Put(new Product { Id = "prod.pils", Name = "Pils 0,3", Unit = "H87", Recipe = [new() { PartId = "prod.fass", Amount = 300, Unit = "MLT" }] });
        rs.Put(new Product { Id = "prod.radler", Name = "Radler 0,5", Unit = "H87", Recipe = [new() { PartId = "prod.fass", Amount = 250, Unit = "MLT" }] });
        rs.Put(new Product { Id = "prod.kranz", Name = "Bierkranz", Unit = "H87", Recipe = [new() { PartId = "prod.fass", Amount = 100, Unit = "MLT" }] });
        rs.Put(new ArticleMapping { Id = "map.kranz", Name = "Bierkranz", ProductId = "prod.kranz", Confirmed = true });
        var cache = new FixedCache().At("Bierkrug", 0.9).At("Krug", 0.5).At("Pils 0,3", 0.88).At("Radler 0,5", 0.87).At("Bierkranz", 0.86)
            .At("Fassbier", 0.8).At("Flaschenbier", 0.1).Query("Pils 30 l");
        var m = new Matcher(Encoders.Shipped, cache);
        var s = await m.Suggest(rs, "", null, new InvoiceLine { Name = "Pils 30 l", UnitCode = "XKG" }, ct: TestContext.Current.CancellationToken);
        Assert.Equal(["prod.krug", "prod.kranz", "prod.fass"], s.Take(3).Select(x => x.Mapping.ProductId));
    }

    [Fact]
    public async Task AProductWithoutCategoryAliasOrMappingIsNoMatchTarget()
    {
        var rs = Rules(Ware("prod.a", "A"), new Product { Id = "prod.bare", Name = "Bare", Unit = "H87" }, Ware("prod.empty", "Empty", ""));
        var s = await Suggest(new FixedCache().At("Bare", 0.95).At("Empty", 0.92).At("A", 0.9), rs);
        Assert.Equal(["prod.a"], Ids(s));
    }
}

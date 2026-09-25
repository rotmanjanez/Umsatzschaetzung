using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public sealed class MatcherFixture
{
    long version = 1_000_000;

    public MemoryCache Cache { get; } = new();
    public Matcher Matcher { get; }
    public RuleSet Seed { get; }

    public MatcherFixture()
    {
        Matcher = new Matcher(Encoders.Shipped, Cache);
        Seed = Rules();
    }

    // The matcher keys its index on the version, so every rule set it sees gets its own.
    public RuleSet Rules(params IRuleEntity[] extra)
    {
        var rs = TestData.Seed();
        foreach (var e in extra) rs.Put(e);
        rs.Version = Interlocked.Increment(ref version);
        return rs;
    }
}

// The ranking the shipped encoder produces on the fixture rule set.
public class MatcherTests(MatcherFixture f) : IClassFixture<MatcherFixture>
{
    const string Rheinland = "Rheinland Getränke Fachgroßhandel GmbH";

    static readonly ArticleMapping Zwickl = new()
    {
        Id = "map.zwickl",
        SupplierName = Rheinland,
        SupplierArticleId = "Z-1",
        Observed = "Zwickl naturtrueb, Keg 30 l",
        IngredientId = "ing.bier.fass",
        Factor = 30000,
        Confirmed = true,
    };

    List<Suggestion> Suggest(string name, string unitCode, RuleSet? rs = null, string gewerbe = "") =>
        f.Matcher.Suggest(rs ?? f.Seed, gewerbe, Rheinland, new InvoiceLine { Name = name, UnitCode = unitCode });

    // Nothing outside the fixture rule set is goods, so a line that is no ware may only be
    // offered the kein-Wareneinsatz ingredients, of which the fixture has none.
    static void NoWare(RuleSet rs, List<Suggestion> s) =>
        Assert.All(s, x => Assert.Equal("cat.kein.wareneinsatz", rs.Ingredients[x.Mapping.IngredientId].CategoryId));

    [Fact]
    public void AKegMapsToTheDraughtBeerWithItsVolume()
    {
        var top = Suggest("Fassbier Pils, Keg 50 l", "XKG")[0];
        Assert.Equal(("ing.bier.fass", 50000L, OriginKind.Encoder, ""), (top.Mapping.IngredientId, top.Mapping.Factor, top.Kind, top.Mapping.Id));
        Assert.InRange(top.Confidence, 1, 99);
    }

    [Fact]
    public void TheBottleSizeBeatsTheAlcoholStrength()
    {
        var top = Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO")[0];
        Assert.Equal(("ing.korn", 700L), (top.Mapping.IngredientId, top.Mapping.Factor));
    }

    [Fact]
    public void ADepositLineIsOfferedNoWare() => NoWare(f.Seed, Suggest("Pfand Leergut Kiste", "XCS"));

    [Fact]
    public void AnUpperCaseWordingScoresLikeTheCatalogues()
    {
        var plain = Suggest("Fassbier Pils, Keg 50 l", "XKG")[0];
        var shouted = Suggest("FASSBIER PILS, KEG 50 L", "XKG")[0];
        Assert.Equal(("ing.bier.fass", 50000L), (shouted.Mapping.IngredientId, shouted.Mapping.Factor));
        Assert.True(shouted.Confidence >= plain.Confidence - 5, $"{plain.Confidence} -> {shouted.Confidence}");
    }

    [Fact]
    public void TheSameLineOnTheSameRulesRanksTheSameWithoutReindexing()
    {
        var rs = f.Rules();
        var first = Suggest("Fassbier Pils, Keg 50 l", "XKG", rs);
        var writes = f.Cache.Writes;
        var again = Suggest("Fassbier Pils, Keg 50 l", "XKG", rs);
        Assert.Equal(first, again, (a, b) => (a.Mapping.IngredientId, a.Mapping.Factor, a.Confidence, a.Kind) == (b.Mapping.IngredientId, b.Mapping.Factor, b.Confidence, b.Kind));
        Assert.Equal(writes, f.Cache.Writes);
    }

    [Fact]
    public void OnlyTheCatalogAndConfirmedWordingsReachTheCache()
    {
        var cache = new MemoryCache();
        var matcher = new Matcher(Encoders.Shipped, cache);
        var automatic = new ArticleMapping { Id = "map.auto", SupplierName = Rheinland, Observed = "Maerzen hell, Keg 30 l", IngredientId = "ing.bier.fass" };
        var rs = f.Rules(Zwickl, automatic);
        matcher.Suggest(rs, "", Rheinland, new InvoiceLine { Name = "Lieferung an Gasthaus Huber, Hauptstr. 3", UnitCode = "H87" });

        Assert.Empty(cache.Read(Encoder.Name, ["Lieferung an Gasthaus Huber, Hauptstr. 3", "Maerzen hell, Keg 30 l"]));
        Assert.Equal(2, cache.Read(Encoder.Name, [rs.Ingredients["ing.korn"].Name, "Zwickl naturtrueb, Keg 30 l"]).Count);
    }

    [Fact]
    public void AFriseurIsNeverOfferedKorn()
    {
        Assert.All(Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO", gewerbe: "96021.0"), x => Assert.NotEqual("ing.korn", x.Mapping.IngredientId));
        Assert.Equal("ing.korn", Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO", gewerbe: "")[0].Mapping.IngredientId);
        Assert.Equal("ing.korn", Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO", gewerbe: "56101.0")[0].Mapping.IngredientId);
    }

    [Fact]
    public void OneConfirmationTeachesTheWordingAndTheNewPackSizeStillDecidesTheFactor()
    {
        var naive = Suggest("Zwickl naturtrueb, Keg 50 l", "XKG").Find(s => s.Mapping.IngredientId == "ing.bier.fass")?.Confidence ?? 0;
        var learnt = f.Rules(Zwickl);
        var top = Suggest("Zwickl naturtrueb, Keg 50 l", "XKG", learnt)[0];
        Assert.Equal(("ing.bier.fass", 50000L, OriginKind.Encoder), (top.Mapping.IngredientId, top.Mapping.Factor, top.Kind));
        Assert.True(top.Confidence >= naive && top.Confidence >= 80, $"{naive} -> {top.Confidence}");
        NoWare(learnt, Suggest("Pfand Leergut Kiste", "XCS", learnt));
    }

    [Fact]
    public void AnExactHitLeadsAndTheEncodersAlternativesFollow()
    {
        var rs = f.Rules(Zwickl);
        var s = f.Matcher.Suggest(rs, "", Rheinland, new InvoiceLine { Name = "Doppelkorn 38 % vol, Flasche 0,7 l", SellerArticleId = "Z-1", UnitCode = "XBO" });
        Assert.True(s.Count > 1);
        Assert.Equal(("map.zwickl", OriginKind.Exact, 100), (s[0].Mapping.Id, s[0].Kind, s[0].Confidence));
        Assert.Equal(("ing.korn", OriginKind.Encoder), (s[1].Mapping.IngredientId, s[1].Kind));
        Assert.All(s.Skip(1), x => Assert.NotEqual("ing.bier.fass", x.Mapping.IngredientId));
    }

    [Fact]
    public void AnUnconfirmedMappingFitsItsArticleWhateverTheWording()
    {
        var rs = f.Rules(new ArticleMapping
        {
            Id = "map.guess", SupplierName = Rheinland, SupplierArticleId = "G-1",
            Observed = "Pils Kiste 20 x 0,5 l", IngredientId = "ing.bier.fass", Confirmed = false,
        });
        var misread = f.Matcher.Suggest(rs, "", Rheinland, new InvoiceLine { Name = "Pils Kiste 20 x 0.5 1", SellerArticleId = "G-1" });
        Assert.Equal(("map.guess", OriginKind.Exact), (misread[0].Mapping.Id, misread[0].Kind));
    }

    [Theory]
    [InlineData("Fassbier Pils, Keg 50 l", "XKG")]
    [InlineData("Flaschenbier Pils 24 x 0,33 l", "XCS")]
    [InlineData("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO")]
    [InlineData("Pfand Leergut Kiste", "XCS")]
    [InlineData("Servietten 3-lagig 250 Stk", "XPK")]
    [InlineData("xq7 zz", "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public void AnEncoderCandidateIsNeverCertainAndNeverBelowTheFloor(string name, string unitCode)
    {
        var s = Suggest(name, unitCode);
        Assert.All(s, x => Assert.Equal(OriginKind.Encoder, x.Kind));
        Assert.All(s, x => Assert.InRange(x.Confidence, 20, 99));
        Assert.Equal(s.Select(x => x.Confidence).OrderDescending(), s.Select(x => x.Confidence));
        Assert.Equal(s.Count, s.Select(x => x.Mapping.IngredientId).Distinct().Count());
    }

    [Fact]
    public void ThePackSizeOfABottleCrateDecidesTheFactor()
    {
        var top = Suggest("Flaschenbier Pils 24 x 0,33 l", "XCS")[0];
        Assert.Equal(("ing.bier.flasche", 7920L), (top.Mapping.IngredientId, top.Mapping.Factor));
    }
}

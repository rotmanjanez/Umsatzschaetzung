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
        ProductId = "prod.bier.fass",
        Factor = 30000,
        Confirmed = true,
    };

    Task<List<Suggestion>> Suggest(string name, string unitCode, RuleSet? rs = null, string gewerbe = "") =>
        f.Matcher.Suggest(rs ?? f.Seed, gewerbe, Rheinland, new InvoiceLine { Name = name, UnitCode = unitCode });

    // Nothing outside the fixture rule set is goods, so a line that is no ware may only be
    // offered the kein-Wareneinsatz products, of which the fixture has none.
    static void NoWare(RuleSet rs, List<Suggestion> s) =>
        Assert.All(s, x => Assert.Equal("cat.kein.wareneinsatz", rs.Products[x.Mapping.ProductId].CategoryId));

    [Fact]
    public async Task AKegMapsToTheDraughtBeerWithItsVolume()
    {
        var top = (await Suggest("Fassbier Pils, Keg 50 l", "XKG"))[0];
        Assert.Equal(("prod.bier.fass", 50000L, OriginKind.Encoder, ""), (top.Mapping.ProductId, top.Mapping.Factor, top.Kind, top.Mapping.Id));
        Assert.InRange(top.Confidence, 1, 99);
    }

    [Fact]
    public async Task TheBottleSizeBeatsTheAlcoholStrength()
    {
        var top = (await Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO"))[0];
        Assert.Equal(("prod.korn", 700L), (top.Mapping.ProductId, top.Mapping.Factor));
    }

    [Fact]
    public async Task ADepositLineIsOfferedNoWare() => NoWare(f.Seed, await Suggest("Pfand Leergut Kiste", "XCS"));

    [Fact]
    public async Task AnUpperCaseWordingScoresLikeTheCatalogues()
    {
        var plain = (await Suggest("Fassbier Pils, Keg 50 l", "XKG"))[0];
        var shouted = (await Suggest("FASSBIER PILS, KEG 50 L", "XKG"))[0];
        Assert.Equal(("prod.bier.fass", 50000L), (shouted.Mapping.ProductId, shouted.Mapping.Factor));
        Assert.True(shouted.Confidence >= plain.Confidence - 5, $"{plain.Confidence} -> {shouted.Confidence}");
    }

    [Fact]
    public async Task TheSameLineOnTheSameRulesRanksTheSameWithoutReindexing()
    {
        var rs = f.Rules();
        var first = await Suggest("Fassbier Pils, Keg 50 l", "XKG", rs);
        var writes = f.Cache.Writes;
        var again = await Suggest("Fassbier Pils, Keg 50 l", "XKG", rs);
        Assert.Equal(first, again, (a, b) => (a.Mapping.ProductId, a.Mapping.Factor, a.Confidence, a.Kind) == (b.Mapping.ProductId, b.Mapping.Factor, b.Confidence, b.Kind));
        Assert.Equal(writes, f.Cache.Writes);
    }

    [Fact]
    public async Task OnlyTheCatalogAndConfirmedWordingsReachTheCache()
    {
        var cache = new MemoryCache();
        var matcher = new Matcher(Encoders.Shipped, cache);
        var automatic = new ArticleMapping { Id = "map.auto", SupplierName = Rheinland, Observed = "Maerzen hell, Keg 30 l", ProductId = "prod.bier.fass" };
        var rs = f.Rules(Zwickl, automatic);
        await matcher.Suggest(rs, "", Rheinland, new InvoiceLine { Name = "Lieferung an Gasthaus Huber, Hauptstr. 3", UnitCode = "H87" }, ct: TestContext.Current.CancellationToken);

        Assert.Empty(cache.Read(Encoder.Name, ["Lieferung an Gasthaus Huber, Hauptstr. 3", "Maerzen hell, Keg 30 l"]));
        Assert.Equal(2, cache.Read(Encoder.Name, [rs.Products["prod.korn"].Name, "Zwickl naturtrueb, Keg 30 l"]).Count);
    }

    [Fact]
    public async Task AFriseurIsNeverOfferedKorn()
    {
        Assert.All(await Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO", gewerbe: "96021.0"), x => Assert.NotEqual("prod.korn", x.Mapping.ProductId));
        Assert.Equal("prod.korn", (await Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO", gewerbe: ""))[0].Mapping.ProductId);
        Assert.Equal("prod.korn", (await Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO", gewerbe: "56101.0"))[0].Mapping.ProductId);
    }

    [Fact]
    public async Task OneConfirmationTeachesTheWordingAndTheNewPackSizeStillDecidesTheFactor()
    {
        var naive = (await Suggest("Zwickl naturtrueb, Keg 50 l", "XKG")).Find(s => s.Mapping.ProductId == "prod.bier.fass")?.Confidence ?? 0;
        var learnt = f.Rules(Zwickl);
        var top = (await Suggest("Zwickl naturtrueb, Keg 50 l", "XKG", learnt))[0];
        Assert.Equal(("prod.bier.fass", 50000L, OriginKind.Encoder), (top.Mapping.ProductId, top.Mapping.Factor, top.Kind));
        Assert.True(top.Confidence >= naive && top.Confidence >= 80, $"{naive} -> {top.Confidence}");
        NoWare(learnt, await Suggest("Pfand Leergut Kiste", "XCS", learnt));
    }

    [Fact]
    public async Task AnExactHitLeadsAndTheEncodersAlternativesFollow()
    {
        var rs = f.Rules(Zwickl);
        var s = await f.Matcher.Suggest(rs, "", Rheinland, new InvoiceLine { Name = "Doppelkorn 38 % vol, Flasche 0,7 l", SellerArticleId = "Z-1", UnitCode = "XBO" }, ct: TestContext.Current.CancellationToken);
        Assert.True(s.Count > 1);
        Assert.Equal(("map.zwickl", OriginKind.Exact, 100), (s[0].Mapping.Id, s[0].Kind, s[0].Confidence));
        Assert.Equal(("prod.korn", OriginKind.Encoder), (s[1].Mapping.ProductId, s[1].Kind));
        Assert.All(s.Skip(1), x => Assert.NotEqual("prod.bier.fass", x.Mapping.ProductId));
    }

    [Fact]
    public async Task AnUnconfirmedMappingFitsItsArticleWhateverTheWording()
    {
        var rs = f.Rules(new ArticleMapping
        {
            Id = "map.guess", SupplierName = Rheinland, SupplierArticleId = "G-1",
            Observed = "Pils Kiste 20 x 0,5 l", ProductId = "prod.bier.fass", Confirmed = false,
        });
        var misread = await f.Matcher.Suggest(rs, "", Rheinland, new InvoiceLine { Name = "Pils Kiste 20 x 0.5 1", SellerArticleId = "G-1" }, ct: TestContext.Current.CancellationToken);
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
    public async Task AnEncoderCandidateIsNeverCertainAndNeverBelowTheFloor(string name, string unitCode)
    {
        var s = await Suggest(name, unitCode);
        Assert.All(s, x => Assert.Equal(OriginKind.Encoder, x.Kind));
        Assert.All(s, x => Assert.InRange(x.Confidence, 20, 99));
        Assert.Equal(s.Select(x => x.Confidence).OrderDescending(), s.Select(x => x.Confidence));
        Assert.Equal(s.Count, s.Select(x => x.Mapping.ProductId).Distinct().Count());
    }

    [Fact]
    public async Task ThePackSizeOfABottleCrateDecidesTheFactor()
    {
        var top = (await Suggest("Flaschenbier Pils 24 x 0,33 l", "XCS"))[0];
        Assert.Equal(("prod.bier.flasche", 7920L), (top.Mapping.ProductId, top.Mapping.Factor));
    }
}

using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

// Which stored rule a line hits. Every wording is orthogonal to every other, so the
// encoder offers nothing and only exact hits come back.
public class MatcherExactTests
{
    const string Rheinland = "Rheinland Getränke Fachgroßhandel GmbH";
    const string Gtin = "4001234567890";

    static async Task<Suggestion?> Hit(RuleSet rs, string? supplier, InvoiceLine line)
    {
        var cache = new FixedCache().Apart([.. rs.Ingredients.Values.Select(i => Matcher.Normal(i.Name))]).Apart(Matcher.Normal(line.Name));
        foreach (var m in rs.Mappings.Values)
            if (Matcher.Wording(m) is { } w) cache.Apart(Matcher.Normal(w));
        var matcher = new Matcher(Encoders.Shipped, cache);
        var s = await matcher.Suggest(rs, "", supplier, line);
        Assert.All(s.Skip(1), x => Assert.Equal(OriginKind.Encoder, x.Kind));
        Assert.All(s.Where(x => x.Kind == OriginKind.Encoder), x => Assert.InRange(x.Confidence, 20, 99));
        return s.Count > 0 && s[0].Kind == OriginKind.Exact ? s[0] : null;
    }

    static RuleSet Seed(params ArticleMapping[] extra)
    {
        var rs = TestData.Seed();
        foreach (var m in extra) rs.Put(m);
        return rs;
    }

    [Fact]
    public async Task TheSuppliersArticleNumberHits()
    {
        var hit = await Hit(Seed(), Rheinland, new InvoiceLine { Name = "Korn 0,7", SellerArticleId = "55120", UnitCode = "XBO" });
        Assert.Equal(("map.korn07", 100, 700L), (hit?.Mapping.Id, hit?.Confidence, hit?.Mapping.Factor));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Anderer Großhandel GmbH")]
    public async Task AnArticleNumberIsOnlyTheSuppliersOwn(string? supplier) =>
        Assert.Null(await Hit(Seed(), supplier, new InvoiceLine { Name = "Korn 0,7", SellerArticleId = "55120", UnitCode = "XBO" }));

    [Theory]
    [InlineData("XBO", true)]
    [InlineData("xbo", true)]
    [InlineData("", true)]
    [InlineData("XCS", false)]
    public async Task AMappingForAnotherUnitDoesNotHit(string unitCode, bool hits) =>
        Assert.Equal(hits, await Hit(Seed(), Rheinland, new InvoiceLine { Name = "Korn", SellerArticleId = "55120", UnitCode = unitCode }) is not null);

    [Fact]
    public async Task AGtinHitsWhoeverSellsIt()
    {
        var rs = Seed(new ArticleMapping { Id = "map.gtin", Gtin = Gtin, IngredientId = "ing.korn", Confirmed = true });
        Assert.Equal("map.gtin", (await Hit(rs, null, new InvoiceLine { Name = "Klarer", Gtin = Gtin }))?.Mapping.Id);
        Assert.Null(await Hit(rs, null, new InvoiceLine { Name = "Klarer", Gtin = "4009999999999" }));
    }

    [Theory]
    [InlineData("Fassbier Pils, Keg")]
    [InlineData("FASSBIER PILS, KEG")]
    [InlineData("fassbier   pils,  keg")]
    [InlineData("Fassbier-Pils, Keg!")]
    public async Task AWordingHitsWhateverItsCaseAndSpacing(string name)
    {
        var rs = Seed(new ArticleMapping { Id = "map.name", Name = "Fassbier Pils, Keg", IngredientId = "ing.bier.fass", Confirmed = true });
        Assert.Equal("map.name", (await Hit(rs, null, new InvoiceLine { Name = name }))?.Mapping.Id);
    }

    [Theory]
    [InlineData("Fassbier Pils Keg")]
    [InlineData("Fassbier Pils, Fass")]
    [InlineData("")]
    public async Task AnotherWordingDoesNotHit(string name)
    {
        var rs = Seed(new ArticleMapping { Id = "map.name", Name = "Fassbier Pils, Keg", IngredientId = "ing.bier.fass", Confirmed = true });
        Assert.Null(await Hit(rs, null, new InvoiceLine { Name = name }));
    }

    [Fact]
    public async Task TheArticleNumberOutranksTheGtinWhichOutranksTheWording()
    {
        var byName = new ArticleMapping { Id = "map.a", Name = "Klarer", IngredientId = "ing.bier.flasche", Confirmed = true };
        var byGtin = new ArticleMapping { Id = "map.b", Gtin = Gtin, IngredientId = "ing.bier.fass", Confirmed = true };
        var byArticle = new ArticleMapping { Id = "map.c", SupplierName = Rheinland, SupplierArticleId = "K-1", IngredientId = "ing.korn", Confirmed = true };
        var rs = Seed(byName, byGtin, byArticle);
        Assert.Equal("map.c", (await Hit(rs, Rheinland, new InvoiceLine { Name = "Klarer", Gtin = Gtin, SellerArticleId = "K-1" }))?.Mapping.Id);
        Assert.Equal("map.b", (await Hit(rs, null, new InvoiceLine { Name = "Klarer", Gtin = Gtin, SellerArticleId = "K-1" }))?.Mapping.Id);
        Assert.Equal("map.a", (await Hit(rs, null, new InvoiceLine { Name = "Klarer" }))?.Mapping.Id);
    }

    [Fact]
    public async Task AMappingNoLongerValidDoesNotHit()
    {
        var rs = Seed();
        rs.Mappings["map.korn07"].Meta.ValidTo = new DateOnly(2020, 1, 1);
        Assert.Null(await Hit(rs, Rheinland, new InvoiceLine { Name = "Korn", SellerArticleId = "55120", UnitCode = "XBO" }));
    }

    [Fact]
    public async Task AnUnconfirmedMappingFitsItsArticleWhateverTheWording()
    {
        var rs = Seed(new ArticleMapping
        {
            Id = "map.guess", SupplierName = Rheinland, SupplierArticleId = "G-1",
            Observed = "Pils Kiste 20 x 0,5 l", IngredientId = "ing.bier.fass", Confirmed = false,
        });
        Assert.Equal("map.guess", (await Hit(rs, Rheinland, new InvoiceLine { Name = "Pils Kiste 20 x 0,5 l", SellerArticleId = "G-1" }))?.Mapping.Id);
        Assert.Equal("map.guess", (await Hit(rs, Rheinland, new InvoiceLine { Name = "Pils Kiste 20 x 0.5 1", SellerArticleId = "G-1" }))?.Mapping.Id);
    }

    [Fact]
    public async Task TheMappingALineCarriesLeads()
    {
        var hit = await Hit(Seed(), null, new InvoiceLine { Name = "Irgendwas", MappingId = "map.fass50" });
        Assert.Equal(("map.fass50", OriginKind.Exact, 100), (hit?.Mapping.Id, hit?.Kind, hit?.Confidence));
    }

    [Fact]
    public async Task AnUnknownCarriedMappingIsNoHit() =>
        Assert.Null(await Hit(Seed(), null, new InvoiceLine { Name = "Irgendwas", MappingId = "map.geloescht" }));
}

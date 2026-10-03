using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class NamesTests
{
    static RuleSet Rules()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.bier", Unit = "MLT", Name = "Fassbier Pils" });
        rs.Put(new Product { Id = "prod.salz", Unit = "GRM", Name = "Salz" });
        rs.Put(new Product { Id = "prod.ei", Unit = "H87", Name = "Ei" });
        rs.Put(new Product
        {
            Id = "p.pils",
            Name = "Pils 0,3 l",
            Recipe = [new() { PartId = "prod.bier", Amount = 300, Unit = "MLT" }, new() { PartId = "prod.salz", Amount = 1500, Unit = "GRM" }],
        });
        rs.Put(new Product { Id = "p.ei", Name = "Spiegelei", Unit = "H87", Recipe = [new() { PartId = "prod.ei", Amount = 2, Unit = "?" }] });
        rs.Put(new ArticleMapping { Id = "map.fass", ProductId = "prod.bier", Factor = 50_000 });
        rs.Put(new ArticleMapping { Id = "map.salz", ProductId = "prod.salz" });
        rs.Put(new ArticleMapping { Id = "map.ei", ProductId = "prod.ei", Factor = 10 });
        return rs;
    }

    [Fact]
    public void AProductFallsBackToItsId()
    {
        var rs = Rules();
        Assert.Equal("Fassbier Pils", Names.Product(rs, "prod.bier"));
        Assert.Equal("prod.weg", Names.Product(rs, "prod.weg"));
        Assert.Equal("Pils 0,3 l", Names.Product(rs, "p.pils"));
        Assert.Equal("p.weg", Names.Product(rs, "p.weg"));
    }

    [Fact]
    public void AProductIsCountedInItsOwnUnitOrInPieces()
    {
        var rs = Rules();
        Assert.Equal(Unit.Ml, Names.ProductUnit(rs, "prod.bier"));
        Assert.Equal(Unit.Piece, Names.ProductUnit(rs, "prod.ei"));
        Assert.Equal(Unit.Piece, Names.ProductUnit(rs, "prod.weg"));
    }

    [Fact]
    public void AnInvoiceIsNamedByItsNumberOrElseItsFile()
    {
        Assert.Equal("RE-1", Names.Invoice(new Invoice { Number = "RE-1", FileName = "a.pdf" }));
        Assert.Equal("a.pdf", Names.Invoice(new Invoice { FileName = "a.pdf" }));
        var c = new Case { Invoices = [new() { Id = "re-1", Number = "RE-1" }, new() { Id = "re-2", FileName = "b.xml" }] };
        Assert.Equal("RE-1", Names.Invoice(c, "re-1"));
        Assert.Equal("b.xml", Names.Invoice(c, "re-2"));
        Assert.Equal("re-9", Names.Invoice(c, "re-9"));
    }

    [Fact]
    public void ARecipeListsAmountUnitAndPart()
    {
        var rs = Rules();
        Assert.Equal("300 Milliliter Fassbier Pils, 1.500 Gramm Salz", Names.Recipe(rs, rs.Products["p.pils"]));
        Assert.Equal("2 Ei", Names.Recipe(rs, rs.Products["p.ei"]));
        Assert.Equal("", Names.Recipe(rs, new Product()));
    }

    [Fact]
    public void AMappingNamesItsProductAndTheContentOfOnePackage()
    {
        var rs = Rules();
        Assert.Equal("Fassbier Pils × 50 l", Names.Mapping(rs, "map.fass"));
        Assert.Equal("Salz", Names.Mapping(rs, "map.salz"));
        Assert.Equal("Ei × 10 Stück", Names.Candidate(rs, rs.Mappings["map.ei"]));
    }

    [Theory]
    [InlineData(null, "ungeklärt")]
    [InlineData("", "ungeklärt")]
    [InlineData("map.weg", "ungeklärt (Zuordnung map.weg unbekannt)")]
    public void AMissingMappingIsUnresolved(string? id, string text) => Assert.Equal(text, Names.Mapping(Rules(), id));
}

using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class NamesTests
{
    static RuleSet Rules()
    {
        var rs = new RuleSet();
        rs.Put(new Ingredient { Id = "ing.bier", Name = "Fassbier Pils" });
        rs.Put(new Ingredient { Id = "ing.salz", Name = "Salz" });
        rs.Put(new Ingredient { Id = "ing.ei", Name = "Ei" });
        rs.Put(new Product
        {
            Id = "p.pils",
            Name = "Pils 0,3 l",
            Recipe = [new() { IngredientId = "ing.bier", Amount = 300, Unit = "MLT" }, new() { IngredientId = "ing.salz", Amount = 1500, Unit = "GRM" }],
        });
        rs.Put(new Product { Id = "p.ei", Name = "Spiegelei", Recipe = [new() { IngredientId = "ing.ei", Amount = 2, Unit = "?" }] });
        rs.Put(new ArticleMapping { Id = "map.fass", IngredientId = "ing.bier", Factor = 50_000 });
        rs.Put(new ArticleMapping { Id = "map.salz", IngredientId = "ing.salz" });
        rs.Put(new ArticleMapping { Id = "map.ei", IngredientId = "ing.ei", Factor = 10 });
        return rs;
    }

    [Fact]
    public void IngredientAndProductFallBackToTheirId()
    {
        var rs = Rules();
        Assert.Equal("Fassbier Pils", Names.Ingredient(rs, "ing.bier"));
        Assert.Equal("ing.weg", Names.Ingredient(rs, "ing.weg"));
        Assert.Equal("Pils 0,3 l", Names.Product(rs, "p.pils"));
        Assert.Equal("p.weg", Names.Product(rs, "p.weg"));
    }

    [Fact]
    public void AnIngredientIsCountedInItsRecipeUnitOrInPieces()
    {
        var rs = Rules();
        Assert.Equal(Unit.Ml, Names.IngredientUnit(rs, "ing.bier"));
        Assert.Equal(Unit.Piece, Names.IngredientUnit(rs, "ing.ei"));
        Assert.Equal(Unit.Piece, Names.IngredientUnit(rs, "ing.weg"));
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
    public void ARecipeListsAmountUnitAndIngredient()
    {
        var rs = Rules();
        Assert.Equal("300 Milliliter Fassbier Pils, 1.500 Gramm Salz", Names.Recipe(rs, rs.Products["p.pils"]));
        Assert.Equal("2 Ei", Names.Recipe(rs, rs.Products["p.ei"]));
        Assert.Equal("", Names.Recipe(rs, new Product()));
    }

    [Fact]
    public void AMappingNamesItsIngredientAndTheContentOfOnePackage()
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

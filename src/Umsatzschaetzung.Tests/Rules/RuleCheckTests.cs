using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;

namespace Umsatzschaetzung.Tests.Rules;

public class RuleCheckTests
{
    static RuleSet Valid()
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "cat.bier", Name = "Bier", Gewerbe = ["561", "56101.0"] });
        rs.Put(new Product { Id = "prod.fassbier", Unit = "MLT", Name = "Pils", CategoryId = "cat.bier" });
        rs.Put(new Product { Id = "prod.salz", Unit = "H87", Name = "Salz" });
        rs.Put(new ArticleMapping { Id = "map.pils", SupplierArticleId = "31090", ProductId = "prod.fassbier", Factor = 50000 });
        rs.Put(new Product { Id = "prod.pils", Name = "Pils 0,3", Unit = "H87", Recipe = [new() { PartId = "prod.fassbier", Amount = 300, Unit = "MLT" }] });
        rs.Put(new YieldRule { Id = "yr.bier", Name = "Bier", CategoryId = "cat.bier", Deduction = 300, Default = true });
        return rs;
    }

    static string Rejected(RuleSet rs) => Assert.Throws<RulesException>(() => RuleCheck.Validate(rs)).Message;

    [Fact]
    public void AConsistentRuleSetPasses() => RuleCheck.Validate(Valid());

    [Fact]
    public void TheFixtureRuleSetPasses() => RuleCheck.Validate(TestData.Seed());

    [Fact]
    public void AnEmptyRuleSetPasses() => RuleCheck.Validate(new RuleSet());

    [Fact]
    public void TheKeyNamesTheEntity()
    {
        var rs = Valid();
        rs.Categories["cat.bier"].Id = "anders";

        RuleCheck.Validate(rs);

        Assert.Equal("cat.bier", rs.Categories["cat.bier"].Id);
    }

    [Fact]
    public void AnEmptyKeyIsRejected()
    {
        var rs = Valid();
        rs.Categories[""] = new Category { Name = "leer" };

        Assert.Contains("leere Entitäts-ID", Rejected(rs));
    }

    [Fact]
    public void AMissingMetaIsFilledIn()
    {
        var rs = Valid();
        rs.Products["prod.salz"].Meta = null!;

        RuleCheck.Validate(rs);

        Assert.NotNull(rs.Products["prod.salz"].Meta);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(1, true)]
    public void ValidToMustLieAfterValidFrom(int days, bool ok)
    {
        var rs = Valid();
        var meta = rs.Products["prod.pils"].Meta;
        meta.ValidFrom = new DateOnly(2024, 1, 1);
        meta.ValidTo = meta.ValidFrom.Value.AddDays(days);

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("gültig bis", Rejected(rs));
    }

    [Fact]
    public void AnOpenEndedValidityPasses()
    {
        var rs = Valid();
        rs.Products["prod.pils"].Meta.ValidTo = new DateOnly(2020, 1, 1);
        rs.Products["prod.fassbier"].Meta.ValidFrom = new DateOnly(2030, 1, 1);

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ACategoryNeedsAName(string name)
    {
        var rs = Valid();
        rs.Categories["cat.bier"].Name = name;

        Assert.Contains("Kategorie: Name", Rejected(rs));
    }

    [Theory]
    [InlineData("5")]
    [InlineData("561")]
    [InlineData("56101")]
    [InlineData("56101.0")]
    public void AGewerbekennzahlOrItsPrefixPasses(string kennzahl)
    {
        var rs = Valid();
        rs.Categories["cat.bier"].Gewerbe = [kennzahl];

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData("561")]
    [InlineData("56101.0")]
    public void AGewerbezweigNeedsAValidKennzahlAndAName(string kennzahl)
    {
        var rs = Valid();
        rs.Put(new Gewerbezweig { Id = "gw", Kennzahl = kennzahl, Name = "Gaststätten" });
        RuleCheck.Validate(rs);

        rs.Gewerbezweige["gw"].Name = " ";
        Assert.Contains("Bezeichnung", Rejected(rs));
        rs.Gewerbezweige["gw"] = new Gewerbezweig { Kennzahl = kennzahl + "x", Name = "Gaststätten" };
        Assert.Contains("ungültig", Rejected(rs));
    }

    [Fact]
    public void AKennzahlIsListedOnce()
    {
        var rs = Valid();
        rs.Put(new Gewerbezweig { Id = "a", Kennzahl = "56101.0", Name = "Gaststätten" });
        rs.Put(new Gewerbezweig { Id = "b", Kennzahl = "56101.0", Name = "Restaurants" });

        Assert.Contains("gibt es schon", Rejected(rs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("561010")]
    [InlineData("56101.")]
    [InlineData("56101.00")]
    [InlineData("Gastro")]
    [InlineData(" 561")]
    public void AnInvalidGewerbekennzahlIsRejected(string kennzahl)
    {
        var rs = Valid();
        rs.Categories["cat.bier"].Gewerbe = ["561", kennzahl];

        Assert.Contains("Gewerbekennzahl", Rejected(rs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void ALeafNeedsAName(string name)
    {
        var rs = Valid();
        rs.Products["prod.salz"].Name = name;

        Assert.Contains("Produkt: Name", Rejected(rs));
    }

    [Fact]
    public void AProductWithADanglingCategoryIsRejected()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.kaputt", Unit = "H87", Name = "Kaputt", CategoryId = "cat.fehlt" });

        Assert.Contains("cat.fehlt", Rejected(rs));
    }

    [Fact]
    public void ALeafNeedsNoCategoryAndNoRecipe()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.serviette", Name = "Serviette", Unit = "H87" });
        rs.Put(new Product { Id = "prod.leer", Name = "Leer", Unit = "GRM", CategoryId = "" });

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Zentner")]
    [InlineData("XBO")]
    public void AProductCountsInAKnownUnit(string unit)
    {
        var rs = Valid();
        rs.Products["prod.salz"].Unit = unit;

        Assert.Contains("keine Einheit", Rejected(rs));
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(-1L, false)]
    [InlineData(1L, true)]
    [InlineData(12L, true)]
    public void ABatchMakesMoreThanNothing(long batch, bool ok)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Batch = batch;

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("ein Ansatz muss mehr als 0", Rejected(rs));
    }

    [Theory]
    [InlineData(0L, Unit.G, false)]
    [InlineData(200L, Unit.Piece, false)]
    [InlineData(200L, Unit.G, true)]
    public void APieceWeighsSomethingInGramsOrMillilitres(long amount, Unit unit, bool ok)
    {
        var rs = Valid();
        rs.Products["prod.salz"].Piece = new Piece(amount, unit);

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("Stückgewicht", Rejected(rs));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData(" ", " ", " - ")]
    public void AMappingNeedsAKey(string? article, string? gtin, string? name)
    {
        var rs = Valid();
        rs.Put(new ArticleMapping { Id = "map.x", SupplierArticleId = article, Gtin = gtin, Name = name, Observed = "Pils", ProductId = "prod.fassbier" });

        Assert.Contains("erforderlich", Rejected(rs));
    }

    [Theory]
    [InlineData("31090", null, null)]
    [InlineData(null, "4001234567890", null)]
    [InlineData(null, null, "Pils Fass")]
    public void AnyOneKeyIsEnoughForAMapping(string? article, string? gtin, string? name)
    {
        var rs = Valid();
        rs.Put(new ArticleMapping { Id = "map.x", SupplierArticleId = article, Gtin = gtin, Name = name, ProductId = "prod.fassbier" });

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(-5L, false)]
    [InlineData(1L, true)]
    [InlineData(null, true)]
    public void AMappingFactorMustBePositiveWhereGiven(long? factor, bool ok)
    {
        var rs = Valid();
        rs.Mappings["map.pils"].Factor = factor;

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("Faktor", Rejected(rs));
    }

    [Theory]
    [InlineData("prod.fehlt")]
    [InlineData("")]
    public void AMappingWithoutAnExistingProductIsRejected(string product)
    {
        var rs = Valid();
        rs.Mappings["map.pils"].ProductId = product;

        Assert.Contains("existiert nicht", Rejected(rs));
    }

    [Fact]
    public void AnyProductMayBeBought()
    {
        var rs = Valid();
        rs.Mappings["map.pils"].ProductId = "prod.pils";

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AProductNeedsAName(string name)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Name = name;

        Assert.Contains("Produkt: Name", Rejected(rs));
    }

    [Fact]
    public void ARecipeWithADanglingPartIsRejected()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.kaputt", Name = "Kaputt", Unit = "H87", Recipe = [new() { PartId = "prod.fehlt", Amount = 1, Unit = "H87" }] });

        Assert.Contains("Bestandteil \"prod.fehlt\" existiert nicht", Rejected(rs));
    }

    [Theory]
    [InlineData(0L, false)]
    [InlineData(-1L, false)]
    [InlineData(1L, true)]
    public void ARecipeAmountMustBePositive(long amount, bool ok)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Recipe[0].Amount = amount;

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("Menge", Rejected(rs));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Zentner")]
    [InlineData("XKG")]
    public void ARecipeUnitMustBeKnown(string unit)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Recipe[0].Unit = unit;

        Assert.Contains("unbekannte Einheit", Rejected(rs));
    }

    [Theory]
    [InlineData("LTR")]
    [InlineData("l")]
    [InlineData(" MLT ")]
    [InlineData("CLT")]
    public void ARecipeUnitMayBeAnyCodeOrAliasOfThePartsScale(string unit)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Recipe[0].Unit = unit;

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData("GRM")]
    [InlineData("H87")]
    public void ARecipeUnitMustConvertToThePartsUnit(string unit)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Recipe[0].Unit = unit;

        Assert.Contains("\"Pils\" zählt in ml", Rejected(rs));
    }

    [Theory]
    [InlineData("H87", true)]
    [InlineData("stk", true)]
    [InlineData("GRM", false)]
    public void APartCountedInPiecesTakesPieces(string unit, bool ok)
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.menu", Name = "Menü", Unit = "H87", Recipe = [new() { PartId = "prod.pils", Amount = 1, Unit = unit }] });

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("\"Pils 0,3\" zählt in Stück", Rejected(rs));
    }

    [Fact]
    public void ARecipeMayNotContainItself()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.a", Name = "A", Unit = "H87", Recipe = [new() { PartId = "prod.b", Amount = 1, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.b", Name = "B", Unit = "H87", Recipe = [new() { PartId = "prod.a", Amount = 1, Unit = "H87" }] });

        Assert.Contains("enthält sich selbst", Rejected(rs));
        rs.Products["prod.b"].Recipe = [new() { PartId = "prod.b", Amount = 1, Unit = "H87" }];
        Assert.Contains("\"B\": das Rezept enthält sich selbst", Rejected(rs));
    }

    // Was gekauft wird, kann selbst ein Rezept haben: der Kreis schließt sich auch über einen eingekauften Bestandteil.
    [Fact]
    public void ACycleThroughABoughtProductIsRejected()
    {
        var rs = Valid();
        rs.Products["prod.fassbier"].Recipe = [new() { PartId = "prod.pils", Amount = 1, Unit = "H87" }];

        Assert.Contains("enthält sich selbst", Rejected(rs));
    }

    [Fact]
    public void ATreeOfAnyDepthPasses()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.runde", Name = "Runde", Unit = "H87", Recipe = [new() { PartId = "prod.menu", Amount = 2, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.menu", Name = "Menü", Unit = "H87", Recipe = [new() { PartId = "prod.pils", Amount = 1, Unit = "H87" }, new() { PartId = "prod.salz", Amount = 1, Unit = "H87" }] });

        RuleCheck.Validate(rs);
    }

    [Fact]
    public void OneScaleInDifferentUnitsPasses()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.pils.05", Name = "Pils 0,5", Unit = "H87", Recipe = [new() { PartId = "prod.fassbier", Amount = 1, Unit = "LTR" }] });
        rs.Put(new Product { Id = "prod.radler", Name = "Radler", Unit = "H87", Recipe = [new() { PartId = "prod.fassbier", Amount = 25, Unit = "CLT" }] });

        RuleCheck.Validate(rs);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void AYieldRuleNeedsAName(string name)
    {
        var rs = Valid();
        rs.YieldRules["yr.bier"].Name = name;

        Assert.Contains("Ausbeuteregel: Name", Rejected(rs));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    public void AYieldRuleNeedsACategoryOrAProduct(string? category, string? product)
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", CategoryId = category, ProductId = product });

        Assert.Contains("Kategorie oder Produkt erforderlich", Rejected(rs));
    }

    [Fact]
    public void AYieldRuleWithADanglingTargetIsRejected()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", ProductId = "prod.fehlt" });
        Assert.Contains("Produkt \"prod.fehlt\"", Rejected(rs));

        rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", CategoryId = "cat.fehlt" });
        Assert.Contains("Kategorie \"cat.fehlt\"", Rejected(rs));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void TheDeductionLiesBetweenNoneAndAHundredPercent(long deduction, bool ok)
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", ProductId = "prod.salz", Deduction = deduction });

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("zwischen 0 und 100 %", Rejected(rs));
    }

    [Fact]
    public void ACategoryHasOneDefaultYieldRule()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.zweit", Name = "Zweite", CategoryId = "cat.bier", Default = true });

        Assert.Contains("Standard ist bereits", Rejected(rs));
    }

    [Fact]
    public void AProductHasOneDefaultYieldRule()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.a", Name = "A", ProductId = "prod.fassbier", Default = true });
        rs.Put(new YieldRule { Id = "yr.b", Name = "B", ProductId = "prod.fassbier", Default = true });

        Assert.Contains("Standard ist bereits", Rejected(rs));
    }

    [Fact]
    public void DefaultsOfDifferentScopesPass()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.nicht", Name = "Kein Standard", CategoryId = "cat.bier" });
        rs.Put(new YieldRule { Id = "yr.pils", Name = "Pils", ProductId = "prod.fassbier", Default = true });
        rs.Put(new YieldRule { Id = "yr.salz", Name = "Salz", ProductId = "prod.salz", Default = true });

        RuleCheck.Validate(rs);
    }

    [Fact]
    public void ACategoryIsUsedByItsProductsAndYieldRulesInNameOrder()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.altbier", Unit = "H87", Name = "Altbier", CategoryId = "cat.bier" });

        Assert.Equal(["Produkt „Altbier“", "Produkt „Pils“", "Ausbeuteregel „Bier“"], RuleCheck.Users(rs, Entity.Category, "cat.bier"));
    }

    [Fact]
    public void AProductIsUsedByMappingsRecipesAndYieldRules()
    {
        var rs = Valid();
        rs.Put(new ArticleMapping { Id = "map.gtin", Gtin = "4001", ProductId = "prod.fassbier" });
        rs.Put(new ArticleMapping { Id = "map.name", Name = "Pils Fass", SupplierArticleId = "1", ProductId = "prod.fassbier" });
        rs.Put(new YieldRule { Id = "yr.pils", Name = "Pils", ProductId = "prod.fassbier" });

        Assert.Equal(
            ["Zuordnung „31090“", "Zuordnung „4001“", "Zuordnung „Pils Fass“", "Produkt „Pils 0,3“", "Ausbeuteregel „Pils“"],
            RuleCheck.Users(rs, Entity.Product, "prod.fassbier"));
    }

    [Fact]
    public void AProductOfTheFixtureThatRecipesUseIsInUse()
    {
        var users = RuleCheck.Users(TestData.Seed(), Entity.Product, "prod.korn");

        Assert.Contains(users, u => u.StartsWith("Produkt „", StringComparison.Ordinal));
        Assert.Contains(users, u => u.StartsWith("Zuordnung „", StringComparison.Ordinal));
    }

    [Fact]
    public void AProductIsUsedByTheRecipesThatContainIt()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.menu", Name = "Menü", Unit = "H87", Recipe = [new() { PartId = "prod.pils", Amount = 1, Unit = "H87" }] });

        Assert.Equal(["Produkt „Menü“"], RuleCheck.Users(rs, Entity.Product, "prod.pils"));
        Assert.DoesNotContain("Produkt „Menü“", RuleCheck.Users(rs, Entity.Product, "prod.fassbier"));
    }

    [Theory]
    [InlineData(Entity.Product, "prod.salz")]
    [InlineData(Entity.Category, "cat.leer")]
    [InlineData(Entity.Mapping, "map.pils")]
    [InlineData(Entity.Product, "prod.pils")]
    [InlineData(Entity.YieldRule, "yr.bier")]
    public void NothingDependsOnAnUnusedEntityOrOnALeaf(Entity kind, string id) =>
        Assert.Empty(RuleCheck.Users(Valid(), kind, id));
}

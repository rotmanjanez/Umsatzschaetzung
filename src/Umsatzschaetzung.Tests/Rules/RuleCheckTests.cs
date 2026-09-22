using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;

namespace Umsatzschaetzung.Tests.Rules;

public class RuleCheckTests
{
    static RuleSet Valid()
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "cat.bier", Name = "Bier", Gewerbe = ["561", "56101.0"] });
        rs.Put(new Ingredient { Id = "ing.pils", Name = "Pils", CategoryId = "cat.bier" });
        rs.Put(new Ingredient { Id = "ing.salz", Name = "Salz" });
        rs.Put(new ArticleMapping { Id = "map.pils", SupplierArticleId = "31090", IngredientId = "ing.pils", Factor = 50000 });
        rs.Put(new Product { Id = "prod.pils", Name = "Pils 0,3", Recipe = [new() { IngredientId = "ing.pils", Amount = 300, Unit = "MLT" }] });
        rs.Put(new YieldRule { Id = "yr.bier", Name = "Bier", CategoryId = "cat.bier", Shrinkage = 300, Default = true });
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
        rs.Ingredients["ing.salz"].Meta = null!;

        RuleCheck.Validate(rs);

        Assert.NotNull(rs.Ingredients["ing.salz"].Meta);
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
        rs.Ingredients["ing.pils"].Meta.ValidFrom = new DateOnly(2030, 1, 1);

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
    public void AnIngredientNeedsAName(string name)
    {
        var rs = Valid();
        rs.Ingredients["ing.salz"].Name = name;

        Assert.Contains("Zutat: Name", Rejected(rs));
    }

    [Fact]
    public void AnIngredientWithADanglingCategoryIsRejected()
    {
        var rs = Valid();
        rs.Put(new Ingredient { Id = "ing.kaputt", Name = "Kaputt", CategoryId = "cat.fehlt" });

        Assert.Contains("cat.fehlt", Rejected(rs));
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", "", "")]
    [InlineData(" ", " ", " - ")]
    public void AMappingNeedsAKey(string? article, string? gtin, string? name)
    {
        var rs = Valid();
        rs.Put(new ArticleMapping { Id = "map.x", SupplierArticleId = article, Gtin = gtin, Name = name, Observed = "Pils", IngredientId = "ing.pils" });

        Assert.Contains("erforderlich", Rejected(rs));
    }

    [Theory]
    [InlineData("31090", null, null)]
    [InlineData(null, "4001234567890", null)]
    [InlineData(null, null, "Pils Fass")]
    public void AnyOneKeyIsEnoughForAMapping(string? article, string? gtin, string? name)
    {
        var rs = Valid();
        rs.Put(new ArticleMapping { Id = "map.x", SupplierArticleId = article, Gtin = gtin, Name = name, IngredientId = "ing.pils" });

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
    [InlineData("ing.fehlt")]
    [InlineData("")]
    public void AMappingWithoutAnExistingIngredientIsRejected(string ingredient)
    {
        var rs = Valid();
        rs.Mappings["map.pils"].IngredientId = ingredient;

        Assert.Contains("existiert nicht", Rejected(rs));
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
    public void AProductNeedsARecipe()
    {
        var rs = Valid();
        rs.Products["prod.pils"].Recipe = [];

        Assert.Contains("Rezept darf nicht leer", Rejected(rs));
        rs.Products["prod.pils"].Recipe = null!;
        Assert.Contains("Rezept darf nicht leer", Rejected(rs));
    }

    [Fact]
    public void ARecipeWithADanglingIngredientIsRejected()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.kaputt", Name = "Kaputt", Recipe = [new() { IngredientId = "ing.fehlt", Amount = 1 }] });

        Assert.Contains("ing.fehlt", Rejected(rs));
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
    public void ARecipeUnitMayBeACodeOrAnAlias(string unit)
    {
        var rs = Valid();
        rs.Products["prod.pils"].Recipe[0].Unit = unit;

        RuleCheck.Validate(rs);
    }

    [Fact]
    public void TwoRecipesMayNotMeasureOneIngredientOnDifferentScales()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.salz", Name = "Salzstange", Recipe = [new() { IngredientId = "ing.salz", Amount = 1, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.brezel", Name = "Brezel", Recipe = [new() { IngredientId = "ing.salz", Amount = 2, Unit = "GRM" }] });

        var message = Rejected(rs);

        Assert.Contains("Salz", message);
        Assert.Contains("\"Brezel\" rechnet in g", message);
        Assert.Contains("\"Salzstange\" in Stück", message);
    }

    [Fact]
    public void OneScaleInDifferentUnitsPasses()
    {
        var rs = Valid();
        rs.Put(new Product { Id = "prod.pils.05", Name = "Pils 0,5", Recipe = [new() { IngredientId = "ing.pils", Amount = 1, Unit = "LTR" }] });
        rs.Put(new Product { Id = "prod.radler", Name = "Radler", Recipe = [new() { IngredientId = "ing.pils", Amount = 25, Unit = "CLT" }, new() { IngredientId = "ing.salz", Amount = 1, Unit = "GRM" }] });

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
    public void AYieldRuleNeedsACategoryOrAnIngredient(string? category, string? ingredient)
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", CategoryId = category, IngredientId = ingredient });

        Assert.Contains("Kategorie oder Zutat erforderlich", Rejected(rs));
    }

    [Fact]
    public void AYieldRuleWithADanglingTargetIsRejected()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", IngredientId = "ing.fehlt" });
        Assert.Contains("Zutat \"ing.fehlt\"", Rejected(rs));

        rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", CategoryId = "cat.fehlt" });
        Assert.Contains("Kategorie \"cat.fehlt\"", Rejected(rs));
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    public void AYieldShareMayNotBeNegative(long shrinkage, long ownUse, long staff, long free)
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", IngredientId = "ing.salz", Shrinkage = shrinkage, OwnUse = ownUse, Staff = staff, Free = free });

        Assert.Contains("nicht negativ", Rejected(rs));
    }

    [Theory]
    [InlineData(2500, 2500, 2500, 2500, true)]
    [InlineData(2500, 2500, 2500, 2501, false)]
    [InlineData(10001, 0, 0, 0, false)]
    [InlineData(0, 0, 0, 0, true)]
    public void TheYieldSharesMayAddUpToAHundredPercentAtMost(long shrinkage, long ownUse, long staff, long free, bool ok)
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.x", Name = "X", IngredientId = "ing.salz", Shrinkage = shrinkage, OwnUse = ownUse, Staff = staff, Free = free });

        if (ok) RuleCheck.Validate(rs);
        else Assert.Contains("100 %", Rejected(rs));
    }

    [Fact]
    public void ACategoryHasOneOpenDefaultYieldRule()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.zweit", Name = "Zweite", CategoryId = "cat.bier", Default = true });

        Assert.Contains("für die Kategorie ist bereits", Rejected(rs));
    }

    [Fact]
    public void AnIngredientHasOneOpenDefaultYieldRule()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.a", Name = "A", IngredientId = "ing.pils", Default = true });
        rs.Put(new YieldRule { Id = "yr.b", Name = "B", IngredientId = "ing.pils", CategoryId = "cat.bier", Default = true });

        Assert.Contains("für die Zutat ist bereits", Rejected(rs));
    }

    [Fact]
    public void DefaultsThatDoNotCollidePass()
    {
        var rs = Valid();
        rs.Put(new YieldRule { Id = "yr.alt", Name = "Alt", CategoryId = "cat.bier", Default = true, Meta = new Meta { ValidTo = new DateOnly(2024, 1, 1) } });
        rs.Put(new YieldRule { Id = "yr.nicht", Name = "Kein Standard", CategoryId = "cat.bier" });
        rs.Put(new YieldRule { Id = "yr.pils", Name = "Pils", IngredientId = "ing.pils", CategoryId = "cat.bier", Default = true });
        rs.Put(new YieldRule { Id = "yr.salz", Name = "Salz", IngredientId = "ing.salz", Default = true });

        RuleCheck.Validate(rs);
    }

    [Fact]
    public void ACategoryIsUsedByItsIngredientsAndYieldRulesInNameOrder()
    {
        var rs = Valid();
        rs.Put(new Ingredient { Id = "ing.altbier", Name = "Altbier", CategoryId = "cat.bier" });

        Assert.Equal(["Zutat „Altbier“", "Zutat „Pils“", "Ausbeuteregel „Bier“"], RuleCheck.Users(rs, Entity.Category, "cat.bier"));
    }

    [Fact]
    public void AnIngredientIsUsedByMappingsRecipesAndYieldRules()
    {
        var rs = Valid();
        rs.Put(new ArticleMapping { Id = "map.gtin", Gtin = "4001", IngredientId = "ing.pils" });
        rs.Put(new ArticleMapping { Id = "map.name", Name = "Pils Fass", SupplierArticleId = "1", IngredientId = "ing.pils" });
        rs.Put(new YieldRule { Id = "yr.pils", Name = "Pils", IngredientId = "ing.pils" });

        Assert.Equal(
            ["Zuordnung „31090“", "Zuordnung „4001“", "Zuordnung „Pils Fass“", "Produkt „Pils 0,3“", "Ausbeuteregel „Pils“"],
            RuleCheck.Users(rs, Entity.Ingredient, "ing.pils"));
    }

    [Fact]
    public void AnIngredientOfTheFixtureThatRecipesUseIsInUse()
    {
        var users = RuleCheck.Users(TestData.Seed(), Entity.Ingredient, "ing.korn");

        Assert.Contains(users, u => u.StartsWith("Produkt „", StringComparison.Ordinal));
        Assert.Contains(users, u => u.StartsWith("Zuordnung „", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(Entity.Ingredient, "ing.salz")]
    [InlineData(Entity.Category, "cat.leer")]
    [InlineData(Entity.Mapping, "map.pils")]
    [InlineData(Entity.Product, "prod.pils")]
    [InlineData(Entity.YieldRule, "yr.bier")]
    public void NothingDependsOnAnUnusedEntityOrOnALeaf(Entity kind, string id) =>
        Assert.Empty(RuleCheck.Users(Valid(), kind, id));
}

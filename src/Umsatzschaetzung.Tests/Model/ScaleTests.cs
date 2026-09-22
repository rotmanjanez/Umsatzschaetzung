using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class ScaleTests
{
    static RuleSet Rules(params (string Product, string Ingredient, long Amount, string Unit)[] recipe)
    {
        var rs = new RuleSet();
        rs.Put(new Ingredient { Id = "ing.bier", Name = "Pils" });
        rs.Put(new Ingredient { Id = "ing.pommes", Name = "Pommes" });
        foreach (var g in recipe.GroupBy(r => r.Product))
            rs.Put(new Product
            {
                Id = g.Key,
                Name = "Produkt " + g.Key,
                Recipe = [.. g.Select(r => new RecipeLine { IngredientId = r.Ingredient, Amount = r.Amount, Unit = r.Unit })],
            });
        return rs;
    }

    [Fact]
    public void TheRecipeUnitDecidesTheBase()
    {
        var rs = Rules(("p.pils", "ing.bier", 300, "MLT"), ("p.mass", "ing.bier", 1, "LTR"), ("p.pommes", "ing.pommes", 150, "GRM"));
        Assert.Equal(Unit.Ml, Scale.Of(rs, "ing.bier"));
        Assert.Equal(Unit.G, Scale.Of(rs, "ing.pommes"));
    }

    [Fact]
    public void AnIngredientInNoRecipeHasNoBase() => Assert.Null(Scale.Of(Rules(("p", "ing.bier", 1, "LTR")), "ing.pommes"));

    [Fact]
    public void RecipesThatDisagreeLeaveNoBase() =>
        Assert.Null(Scale.Of(Rules(("p.a", "ing.pommes", 150, "GRM"), ("p.b", "ing.pommes", 1, "H87")), "ing.pommes"));

    [Fact]
    public void AnUnknownRecipeUnitLeavesNoBase() =>
        Assert.Null(Scale.Of(Rules(("p.a", "ing.pommes", 150, "GRM"), ("p.b", "ing.pommes", 1, "Schaufel")), "ing.pommes"));

    [Fact]
    public void ConflictsNameBothProductsAndBothUnits()
    {
        var rs = Rules(("p.a", "ing.pommes", 150, "GRM"), ("p.b", "ing.pommes", 1, "H87"), ("p.c", "ing.bier", 1, "Schaufel"));
        var flags = Scale.Conflicts(rs, ["ing.pommes", "ing.bier"]);
        Assert.Equal(["recipe_unit_conflict", "unknown_recipe_unit"], flags.Select(f => f.Code));
        Assert.Equal("„Pommes“ wird in „Produkt p.a“ in g und in „Produkt p.b“ in Stück gerechnet", flags[0].Message);
        Assert.Equal("Rezeptur „Produkt p.c“: „Pils“ hat die unbekannte Einheit „Schaufel“", flags[1].Message);
    }

    [Fact]
    public void ConflictsLookOnlyAtTheWantedIngredients()
    {
        var rs = Rules(("p.a", "ing.pommes", 150, "GRM"), ("p.b", "ing.pommes", 1, "H87"));
        Assert.Empty(Scale.Conflicts(rs, ["ing.bier"]));
        Assert.Empty(Scale.Conflicts(Rules(("p.a", "ing.pommes", 150, "GRM"), ("p.b", "ing.pommes", 1, "KGM")), ["ing.pommes"]));
    }

    [Fact]
    public void AConflictNamesAnUnknownIngredientById()
    {
        var flags = Scale.Conflicts(Rules(("p.a", "ing.x", 1, "?")), ["ing.x"]);
        Assert.Contains("„ing.x“", Assert.Single(flags).Message);
    }

    [Theory]
    [InlineData(3, "KGM", 3000)]
    [InlineData(3, "kg", 3000)]
    [InlineData(2, "CLT", 20)]
    [InlineData(-2, "LTR", -2000)]
    [InlineData(3, "XBO", 3)]
    [InlineData(3, "Schaufel", 3)]
    [InlineData(0, "KGM", 0)]
    public void ToBaseMultipliesByTheUnitFactor(long amount, string unit, long expected) =>
        Assert.Equal(expected, Scale.ToBase(amount, unit));
}

using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Calc;

// Eine Rezeptur der Prüfung geht der des Katalogs vor, ohne den Katalog anzufassen.
public class RecipeTests
{
    const string Flasche = "prod.pils.flasche";
    static readonly RuleSet Rules = TestData.Seed();
    static readonly Report Catalog = Calculation.Run(Vorlage.Load(), Rules);

    static Case Adjusted(string ingredientId, long amount)
    {
        var c = Vorlage.Load();
        var cp = c.Products.Single(p => p.ProductId == Flasche);
        cp.Recipe = [new RecipeLine { IngredientId = ingredientId, Amount = amount, Unit = "MLT" }];
        cp.RecipeBasis = Rules.Products[Flasche].Meta.Rev;
        return c;
    }

    static ProductRow Row(Report r) => r.Products.Single(p => p.ProductId == Flasche);

    [Fact]
    public void ALargerAmountYieldsFewerPortions()
    {
        var r = Calculation.Run(Adjusted("ing.bier.flasche", 500), Rules);

        Assert.True(Row(Catalog).Portions > 0);
        Assert.True(Row(r).Portions < Row(Catalog).Portions);
    }

    [Fact]
    public void ASubstituteIngredientIsConsumedInsteadOfTheCatalogOne()
    {
        var r = Calculation.Run(Adjusted("ing.bier.fass", 300), Rules);

        Assert.Contains(Catalog.Ingredients, i => i.IngredientId == "ing.bier.flasche" && i.Used > 0);
        Assert.DoesNotContain(r.Ingredients, i => i.IngredientId == "ing.bier.flasche");
        Assert.Contains(r.Unused, l => l.IngredientId == "ing.bier.flasche");
        Assert.Equal(r.Products.Single(p => p.ProductId == "prod.pils.03").CostPerPortion, Row(r).CostPerPortion);
        Assert.NotEqual(Row(Catalog).CostPerPortion, Row(r).CostPerPortion);
    }

    [Fact]
    public void WithoutAnOverrideTheCatalogIsReturnedAsItIs()
    {
        var c = Vorlage.Load();

        Assert.Same(Rules, Recipes.Effective(c, Rules));
    }

    [Fact]
    public void AnOverrideLeavesTheCatalogUntouched()
    {
        var rules = TestData.Seed();
        var before = rules.Products[Flasche].Recipe;

        var effective = Recipes.Effective(Adjusted("ing.bier.fass", 330), rules);

        Assert.NotSame(rules, effective);
        Assert.Same(before, rules.Products[Flasche].Recipe);
        Assert.Equal("ing.bier.flasche", Assert.Single(rules.Products[Flasche].Recipe).IngredientId);
        Assert.Equal("ing.bier.fass", Assert.Single(effective.Products[Flasche].Recipe).IngredientId);
        Assert.Same(rules.Products["prod.korn.2cl"], effective.Products["prod.korn.2cl"]);
    }

    [Fact]
    public void AChangedCatalogProductMakesTheOverrideStale()
    {
        var rules = TestData.Seed();
        var cp = Adjusted("ing.bier.flasche", 500).Products.Single(p => p.ProductId == Flasche);
        Assert.False(Recipes.Stale(cp, rules));

        rules.Products[Flasche].Meta.Rev = cp.RecipeBasis + 1;

        Assert.True(Recipes.Stale(cp, rules));
    }

    [Fact]
    public void TheReportPrintsTheCasesRecipe()
    {
        var c = Adjusted("ing.bier.flasche", 500);

        var html = Html.Render(c, Rules, Calculation.Run(c, Rules), null);

        Assert.Contains("<span>500 Milliliter Flaschenbier Pils 0,33 l</span>", html);
        Assert.DoesNotContain("<span>330 Milliliter Flaschenbier Pils 0,33 l</span>", html);
    }
}

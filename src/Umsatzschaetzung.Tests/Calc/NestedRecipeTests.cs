using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Calc;

// Ein Rezept darf andere Produkte in Portionen nennen; die Kalkulation sieht nur Zutaten.
public class NestedRecipeTests
{
    const string Set = "prod.bierset";

    static RecipeLine Part(string productId, long portions = 1) => new() { ProductId = productId, Amount = portions, Unit = "H87" };

    static RecipeLine Line(string ingredientId, long amount, string unit) => new() { IngredientId = ingredientId, Amount = amount, Unit = unit };

    static RuleSet Nested()
    {
        var rules = TestData.Seed();
        rules.Put(new Product { Id = Set, Name = "Bierset", Recipe = [Part("prod.pils.03", 2), Line("ing.korn", 20, "MLT")] });
        return rules;
    }

    static (string, long, string)[] Lines(List<RecipeLine> recipe) => [.. recipe.Select(l => (l.IngredientId, l.Amount, l.Unit))];

    static Case Selling(string productId)
    {
        var c = Vorlage.Load();
        c.Products = [new CaseProduct { ProductId = productId, GrossPrice = 500, Vat = 1900 }];
        return c;
    }

    [Fact]
    public void APartIsResolvedToItsIngredientsAtTheEntry()
    {
        var rules = Nested();

        var flat = Recipes.Effective(Vorlage.Load(), rules).Products[Set].Recipe;

        Assert.Equal([("ing.bier.fass", 600L, "MLT"), ("ing.korn", 20L, "MLT")], Lines(flat));
        Assert.Equal("prod.pils.03", rules.Products[Set].Recipe[0].ProductId);
    }

    [Fact]
    public void OneIngredientFromTwoPartsIsSummed()
    {
        var rules = TestData.Seed();
        rules.Put(new Product { Id = "prod.paar", Name = "Paar", Recipe = [Part("prod.pils.03"), Part("prod.pils.05")] });

        Assert.Equal([("ing.bier.fass", 800L, "MLT")], Lines(Recipes.Flat(rules, rules.Products["prod.paar"])));
    }

    [Fact]
    public void PartsNestToAnyDepth()
    {
        var rules = Nested();
        rules.Put(new Product { Id = "prod.runde", Name = "Runde", Recipe = [Part(Set, 2)] });

        Assert.Equal([("ing.bier.fass", 1200L, "MLT"), ("ing.korn", 40L, "MLT")], Lines(Recipes.Flat(rules, rules.Products["prod.runde"])));
    }

    [Fact]
    public void ARecipeWithoutPartsIsReturnedAsItIs()
    {
        var rules = TestData.Seed();
        var p = rules.Products["prod.pils.03"];

        Assert.Same(p.Recipe, Recipes.Flat(rules, p));
        Assert.Same(rules, Recipes.Effective(Vorlage.Load(), rules));
    }

    [Fact]
    public void ARecipeContainingItselfIsRefused()
    {
        var rules = TestData.Seed();
        rules.Put(new Product { Id = "prod.a", Name = "A", Recipe = [Part("prod.b")] });
        rules.Put(new Product { Id = "prod.b", Name = "B", Recipe = [Part("prod.a")] });

        Assert.Throws<InvalidOperationException>(() => Recipes.Flat(rules, rules.Products["prod.a"]));
    }

    [Fact]
    public void ANestedProductSellsLikeItsFlatTwin()
    {
        var rules = Nested();
        rules.Put(new Product { Id = "prod.flach", Name = "Bierset flach", Recipe = [Line("ing.bier.fass", 600, "MLT"), Line("ing.korn", 20, "MLT")] });

        var nested = Calculation.Run(Selling(Set), rules).Products.Single(p => p.ProductId == Set);
        var flat = Calculation.Run(Selling("prod.flach"), rules).Products.Single(p => p.ProductId == "prod.flach");

        Assert.True(nested.Portions > 0);
        Assert.Equal((flat.Portions, flat.CostPerPortion, flat.RevenueNet), (nested.Portions, nested.CostPerPortion, nested.RevenueNet));
    }

    [Fact]
    public void TheCatalogRecipeBesideAnAdjustmentIsFlat()
    {
        var rules = Nested();
        var c = Selling(Set);
        c.Products[0].Recipe = [Line("ing.bier.fass", 500, "MLT")];
        c.Products[0].RecipeBasis = rules.Products[Set].Meta.Rev;

        var row = Calculation.Run(c, rules).Products.Single(p => p.ProductId == Set);

        Assert.True(row.RecipeAdjusted);
        Assert.Equal([("ing.bier.fass", 600L, "MLT"), ("ing.korn", 20L, "MLT")], Lines(row.CatalogRecipe));
    }

    [Fact]
    public void TheListNamesAPartInPortions()
    {
        var rules = Nested();

        Assert.Equal("2 Portionen Pils 0,3 l vom Fass, 20 Milliliter Doppelkorn 38 % vol", Names.Recipe(rules, rules.Products[Set]));
    }
}

using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Calc;

// Ein Rezept darf Produkte mit eigenem Rezept nennen; die Kalkulation sieht nur Produkte ohne.
public class NestedRecipeTests
{
    const string Set = "prod.bierset";

    static PartLine Part(string productId, long portions = 1) => new() { PartId = productId, Amount = portions, Unit = "H87" };

    static PartLine Line(string partId, long amount, string unit) => new() { PartId = partId, Amount = amount, Unit = unit };

    static RuleSet Nested()
    {
        var rules = TestData.Seed();
        rules.Put(new Product { Id = Set, Name = "Bierset", Unit = "H87", Recipe = [Part("prod.pils.03", 2), Line("prod.korn", 20, "MLT")] });
        return rules;
    }

    static (string, long, string)[] Lines(List<PartLine> recipe) => [.. recipe.Select(l => (l.PartId, l.Amount, l.Unit))];

    static Case Selling(string productId)
    {
        var c = Vorlage.Load();
        c.Products = [new CaseProduct { ProductId = productId, GrossPrice = 500, Vat = 1900 }];
        return c;
    }

    [Fact]
    public void APartIsResolvedToProductsWithoutARecipe()
    {
        var rules = Nested();

        Assert.Equal([("prod.bier.fass", 600L, "MLT"), ("prod.korn", 20L, "MLT")], Lines(Recipes.Sold(rules, rules.Products[Set])));
        Assert.Equal("prod.pils.03", Recipes.Effective(Vorlage.Load(), rules).Products[Set].Recipe[0].PartId);
    }

    [Fact]
    public void OneLeafFromTwoPartsIsSummed()
    {
        var rules = TestData.Seed();
        rules.Put(new Product { Id = "prod.paar", Name = "Paar", Unit = "H87", Recipe = [Part("prod.pils.03"), Part("prod.pils.05")] });

        Assert.Equal([("prod.bier.fass", 800L, "MLT")], Lines(Recipes.Sold(rules, rules.Products["prod.paar"])));
    }

    [Fact]
    public void PartsNestToAnyDepth()
    {
        var rules = Nested();
        rules.Put(new Product { Id = "prod.runde", Name = "Runde", Unit = "H87", Recipe = [Part(Set, 2)] });

        Assert.Equal([("prod.bier.fass", 1200L, "MLT"), ("prod.korn", 40L, "MLT")], Lines(Recipes.Sold(rules, rules.Products["prod.runde"])));
    }

    [Fact]
    public void ARecipeWithoutPartsIsReturnedAsItIs()
    {
        var rules = TestData.Seed();
        var p = rules.Products["prod.pils.03"];

        Assert.Equal(Lines(p.Recipe), Lines(Recipes.Sold(rules, p)));
        Assert.Same(rules, Recipes.Effective(Vorlage.Load(), rules));
    }

    [Fact]
    public void ARecipeContainingItselfIsRefused()
    {
        var rules = TestData.Seed();
        rules.Put(new Product { Id = "prod.a", Name = "A", Unit = "H87", Recipe = [Part("prod.b")] });
        rules.Put(new Product { Id = "prod.b", Name = "B", Unit = "H87", Recipe = [Part("prod.a")] });

        Assert.Throws<InvalidOperationException>(() => Recipes.Sold(rules, rules.Products["prod.a"]));
    }

    [Fact]
    public void ANestedProductSellsLikeItsFlatTwin()
    {
        var rules = Nested();
        rules.Put(new Product { Id = "prod.flach", Name = "Bierset flach", Unit = "H87", Recipe = [Line("prod.bier.fass", 600, "MLT"), Line("prod.korn", 20, "MLT")] });

        var nested = Calculation.Run(Selling(Set), rules).Products.Single(p => p.ProductId == Set);
        var flat = Calculation.Run(Selling("prod.flach"), rules).Products.Single(p => p.ProductId == "prod.flach");

        Assert.True(nested.Portions > 0);
        Assert.Equal((flat.Portions, flat.CostPerPortion, flat.RevenueNet), (nested.Portions, nested.CostPerPortion, nested.RevenueNet));
    }

    [Fact]
    public void TheListNamesEveryPartInItsUnit()
    {
        var rules = Nested();

        Assert.Equal("2 Stück Pils 0,3 l vom Fass, 20 Milliliter Doppelkorn 38 % vol", Names.Recipe(rules, rules.Products[Set]));
    }

    // Ein Rezept gilt für einen Ansatz: 1 kg Teig aus 600 g Mehl und 400 ml Wasser, die Pizza nimmt 250 g davon.
    [Fact]
    public void APartsRecipeIsScaledByItsBatch()
    {
        var rules = new RuleSet();
        rules.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        rules.Put(new Product { Id = "prod.wasser", Name = "Wasser", Unit = "MLT" });
        rules.Put(new Product { Id = "prod.teig", Name = "Teig", Unit = "KGM", Batch = 1, Recipe = [Line("prod.mehl", 600, "GRM"), Line("prod.wasser", 400, "MLT")] });
        rules.Put(new Product { Id = "prod.pizza", Name = "Pizza", Unit = "H87", Recipe = [Line("prod.teig", 250, "GRM")] });

        Assert.Equal([("prod.mehl", 150L, "GRM"), ("prod.wasser", 100L, "MLT")], Lines(Recipes.Sold(rules, rules.Products["prod.pizza"])));
        rules.Products["prod.teig"].Unit = "GRM";
        rules.Products["prod.teig"].Batch = 2000;
        Assert.Equal([("prod.mehl", 75L, "GRM"), ("prod.wasser", 50L, "MLT")], Lines(Recipes.Sold(rules, rules.Products["prod.pizza"])));
    }

    [Fact]
    public void ASoldBatchIsSplitIntoItsUnits()
    {
        var rules = new RuleSet();
        rules.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        rules.Put(new Product { Id = "prod.kuchen", Name = "Kuchenstück", Unit = "H87", Batch = 12, Recipe = [Line("prod.mehl", 600, "GRM")] });

        Assert.Equal([("prod.mehl", 50L, "GRM")], Lines(Recipes.Sold(rules, rules.Products["prod.kuchen"])));
    }

    [Fact]
    public void ASoldProductWithoutARecipeUsesUpItself()
    {
        var rules = new RuleSet();
        rules.Put(new Category { Id = "cat.suess", Name = "Süßwaren", Sparte = Sparte.Handelsware });
        rules.Put(new Product { Id = "prod.riegel", Name = "Schokoriegel", Unit = "H87", CategoryId = "cat.suess" });
        rules.Put(new ArticleMapping { Id = "map.riegel", Name = "Schokoriegel", ProductId = "prod.riegel", Confirmed = true });
        var c = new Case
        {
            PeriodFrom = new DateOnly(2024, 1, 1),
            PeriodTo = new DateOnly(2024, 12, 31),
            Invoices = [new Invoice { Id = "inv.1", Number = "R-1", Date = new DateOnly(2024, 5, 2), Lines = [new() { No = 1, Name = "Schokoriegel", Quantity = 24_000, UnitCode = "H87", LineNet = 1_200 }] }],
            Products = [new() { ProductId = "prod.riegel", GrossPrice = 107, Vat = 700 }],
        };

        Assert.Equal([("prod.riegel", 1L, "H87")], Lines(Recipes.Sold(rules, rules.Products["prod.riegel"])));
        Assert.Empty(rules.Products["prod.riegel"].Recipe);
        var sold = Assert.Single(Calculation.Run(c, rules).Products);
        Assert.Equal((24L, 2_400L), (sold.Portions, sold.RevenueNet));
        Assert.Equal(Sparte.Handelsware, sold.Sparte);

        c.Products[0].Recipe = [];
        Assert.Equal(24, Assert.Single(Calculation.Run(c, rules).Products).Portions);
    }
}


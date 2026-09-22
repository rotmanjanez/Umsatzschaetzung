using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Calc;

public class MarkupTests
{
    static RuleSet Rules()
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "cat.getraenke", Name = "Getränke", Sparte = Sparte.Getränke });
        rs.Put(new Category { Id = "cat.tabak", Name = "Tabak", Sparte = Sparte.Handelsware });
        rs.Put(new Ingredient { Id = "ing.cola", Name = "Cola", CategoryId = "cat.getraenke" });
        rs.Put(new Ingredient { Id = "ing.zigaretten", Name = "Zigaretten", CategoryId = "cat.tabak" });
        rs.Put(new ArticleMapping { Id = "map.cola", Name = "Cola", IngredientId = "ing.cola", Confirmed = true });
        rs.Put(new ArticleMapping { Id = "map.zigaretten", Name = "Zigaretten", IngredientId = "ing.zigaretten", Confirmed = true });
        rs.Put(new Product { Id = "prod.cola", Name = "Cola", Recipe = [new() { IngredientId = "ing.cola", Amount = 1, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.zigaretten", Name = "Zigaretten", Recipe = [new() { IngredientId = "ing.zigaretten", Amount = 1, Unit = "H87" }] });
        return rs;
    }

    static Case Kase(string gewerbe) => new()
    {
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Taxpayer = new Taxpayer { Gewerbe = gewerbe },
        Invoices =
        [
            new Invoice
            {
                Id = "inv.1",
                Lines =
                [
                    new() { No = 1, Name = "Cola", Quantity = 100_000, UnitCode = "H87", LineNet = 5_000 },
                    new() { No = 2, Name = "Zigaretten", Quantity = 100_000, UnitCode = "H87", LineNet = 60_000 },
                ],
            },
        ],
        Products =
        [
            new() { ProductId = "prod.cola", GrossPrice = 238, Vat = 1900 },
            new() { ProductId = "prod.zigaretten", GrossPrice = 952, Vat = 1900 },
        ],
    };

    [Fact]
    public void GastronomySplitsTheMarkupBySparte()
    {
        var r = Calculation.Run(Kase("56101.0"), Rules());
        Assert.Equal([Sparte.Getränke, Sparte.Handelsware], r.Markups.Select(m => m.Sparte));
        Assert.Equal(30_000, r.Markups[0].Markup);
        Assert.Equal(3_333, r.Markups[1].Markup);
        Assert.DoesNotContain(r.Warnings, w => w.Code == "sparte-missing");
    }

    [Fact]
    public void RetailHasOneMarkupForTheWholeBusiness()
    {
        var retail = Calculation.Run(Kase("47260.0"), Rules());
        var gastro = Calculation.Run(Kase("56101.0"), Rules());
        Assert.Empty(retail.Markups);
        Assert.Equal(gastro.Totals.Markup, retail.Totals.Markup);
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("55101.0", true)]
    [InlineData("561", true)]
    [InlineData("56309.0", true)]
    [InlineData("5", false)]
    [InlineData("47710.0", false)]
    public void GastronomyIsReadFromTheKennzahl(string kennzahl, bool gastronomie) =>
        Assert.Equal(gastronomie, Gewerbe.Gastronomie(kennzahl));
}

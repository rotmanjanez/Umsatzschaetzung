using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Calc;

public class EstimateTests
{
    static RuleSet Rules()
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "cat.getraenke", Name = "Getränke", Sparte = Sparte.Getränke });
        rs.Put(new Category { Id = "cat.tabak", Name = "Tabak", Sparte = Sparte.Handelsware });
        rs.Put(new Category { Id = "cat.sonst", Name = "Sonstiges" });
        foreach (var (id, name, cat) in new[]
                 {
                     ("ing.cola", "Cola", "cat.getraenke"), ("ing.bier", "Bier", "cat.getraenke"), ("ing.wein", "Wein", "cat.getraenke"),
                     ("ing.sirup", "Sirup", "cat.getraenke"), ("ing.zigaretten", "Zigaretten", "cat.tabak"), ("ing.fracht", "Fracht", "cat.sonst"),
                 })
        {
            rs.Put(new Ingredient { Id = id, Name = name, CategoryId = cat });
            rs.Put(new ArticleMapping { Id = "map" + id[3..], Name = name, IngredientId = id, Confirmed = true });
        }
        rs.Put(new Product { Id = "prod.cola", Name = "Cola", Recipe = [new() { IngredientId = "ing.cola", Amount = 1, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.zigaretten", Name = "Zigaretten", Recipe = [new() { IngredientId = "ing.zigaretten", Amount = 1, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.bier", Name = "Bier", Recipe = [new() { IngredientId = "ing.bier", Amount = 1, Unit = "H87" }] });
        rs.Put(new Product { Id = "prod.wein", Name = "Wein", Recipe = [new() { IngredientId = "ing.wein", Amount = 3, Unit = "H87" }] });
        return rs;
    }

    static Case Kase(params InvoiceLine[] extra) => new()
    {
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Taxpayer = new Taxpayer { Gewerbe = "56101.0" },
        Invoices =
        [
            new Invoice
            {
                Id = "inv.1",
                Number = "R-1",
                Date = new DateOnly(2024, 3, 1),
                Lines =
                [
                    new() { No = 1, Name = "Cola", Quantity = 100_000, UnitCode = "H87", LineNet = 5_000 },
                    new() { No = 2, Name = "Zigaretten", Quantity = 100_000, UnitCode = "H87", LineNet = 60_000 },
                    .. extra,
                ],
            },
        ],
        Products =
        [
            new() { ProductId = "prod.cola", GrossPrice = 238, Vat = 1900 },
            new() { ProductId = "prod.zigaretten", GrossPrice = 952, Vat = 700 },
            new() { ProductId = "prod.bier", GrossPrice = 0, Vat = 1900 },
        ],
    };

    [Fact]
    public void ProductsWithoutPriceStayOutOfTheMarkupAndCarryTheirSparte()
    {
        var r = Calculation.Run(Kase(new InvoiceLine { No = 3, Name = "Bier", Quantity = 100_000, UnitCode = "H87", LineNet = 10_000 }), Rules());
        Assert.Equal(30_000, r.Markups.Single(m => m.Sparte == Sparte.Getränke).Markup);
        var e = Assert.Single(r.Estimated);
        Assert.Equal((EstimateSource.PriceMissing, Sparte.Getränke, 10_000L, 30_000L, 40_000L), (e.Source, e.Basis, e.Cost, e.Markup, e.RevenueNet));
        Assert.Equal(r.Totals.CalculatedRevenueNet + 40_000, r.Totals.RevenueNet);
    }

    [Fact]
    public void LinesOutsideTheRecipesCarryTheirSparteOrTheOverallRate()
    {
        var r = Calculation.Run(Kase(
            new InvoiceLine { No = 3, Name = "Sirup", Quantity = 1_000, UnitCode = "H87", LineNet = 1_000 },
            new InvoiceLine { No = 4, Name = "Fracht", Quantity = 1_000, UnitCode = "H87", LineNet = 1_000 }), Rules());
        var sirup = r.Estimated.Single(e => e.Name == "Sirup");
        var fracht = r.Estimated.Single(e => e.Name == "Fracht");
        Assert.Equal((EstimateSource.Unused, Sparte.Getränke, 4_000L, "R-1"), (sirup.Source, sirup.Basis, sirup.RevenueNet, sirup.Invoice));
        Assert.Equal((Sparte.Unbestimmt, r.Totals.Markup), (fracht.Basis, fracht.Markup));
        Assert.Equal(0, r.Totals.ExcludedShare);
    }

    [Fact]
    public void UnallocatedWareIsEstimatedPerIngredient()
    {
        var c = Kase(new InvoiceLine { No = 3, Name = "Wein", Quantity = 100_000, UnitCode = "H87", LineNet = 10_000 });
        c.Products.Add(new() { ProductId = "prod.wein", GrossPrice = 1_190, Vat = 1900 });
        var r = Calculation.Run(c, Rules());
        var e = Assert.Single(r.Estimated);
        Assert.Equal((EstimateSource.Leftover, "Wein", 1L, 100L), (e.Source, e.Name, e.Qty, e.Cost));
        Assert.Equal(r.Markups.Single(m => m.Sparte == Sparte.Getränke).Markup, e.Markup);
    }

    [Fact]
    public void EstimatedRevenueFollowsTheVatOfItsSparte()
    {
        var c = Kase(
            new InvoiceLine { No = 3, Name = "Sirup", Quantity = 1_000, UnitCode = "H87", LineNet = 1_000 },
            new InvoiceLine { No = 4, Name = "Fracht", Quantity = 1_000, UnitCode = "H87", LineNet = 1_000 });
        var r = Calculation.Run(c, Rules());
        var rows = VatRow.Of(c, r);
        var fracht = r.Estimated.Single(e => e.Name == "Fracht").RevenueNet;
        var tobacco = r.Products.Single(p => p.ProductId == "prod.zigaretten").RevenueNet;
        var toFull = 20_000 + 4_000 + fracht * 20_000 / (20_000 + tobacco);
        Assert.Equal(toFull, rows.Single(v => v.Vat == 1900 && !v.Total).Calculated);
        Assert.Equal(r.Totals.RevenueNet, rows.Single(v => v.Total).Calculated);
    }

    [Fact]
    public void IngredientsWithoutRevenueLeaveTheEstimateAndTheWareneinsatz()
    {
        var c = Kase(
            new InvoiceLine { No = 3, Name = "Sirup", Quantity = 1_000, UnitCode = "H87", LineNet = 1_000 },
            new InvoiceLine { No = 4, Name = "Sirup", Quantity = 2_000, UnitCode = "XBO", LineNet = 1_500 });
        var before = Calculation.Run(c, Rules());
        c.NoRevenue = ["ing.sirup", "ing.cola"];
        var r = Calculation.Run(c, Rules());
        Assert.Empty(r.Estimated);
        Assert.Equal(["Cola", "Sirup", "Sirup"], r.NoRevenue.Select(l => l.Name).Order());
        Assert.Equal(7_500, r.Totals.NoRevenueCost);
        Assert.Equal(before.Totals.CostOfGoods - 5_000, r.Totals.CostOfGoods);
        Assert.Equal(7_500 * Bp.Full / r.Totals.Purchases, r.Totals.ExcludedShare);
    }

    [Fact]
    public void WithoutAnyPriceThereIsNoMarkupToEstimateWith()
    {
        var c = Kase(new InvoiceLine { No = 3, Name = "Bier", Quantity = 100_000, UnitCode = "H87", LineNet = 10_000 });
        c.Products = [new() { ProductId = "prod.bier", Vat = 1900 }];
        var r = Calculation.Run(c, Rules());
        Assert.Equal(0, r.Totals.EstimatedRevenueNet);
        Assert.Equal(75_000, r.Totals.EstimatedCost);
        Assert.Contains(r.Warnings, w => w.Code == "markup-missing");
    }
}

using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;

namespace Umsatzschaetzung.Tests.Calc;

public class PieceWeightTests
{
    static RuleSet Rules(Piece? piece)
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "cat.gemuese", Name = "Gemüse", Sparte = Sparte.Speisen });
        rs.Put(new Ingredient { Id = "ing.gurke", Name = "Gurken", CategoryId = "cat.gemuese", Piece = piece });
        rs.Put(new ArticleMapping { Id = "map.gurke", Name = "Salatgurke", IngredientId = "ing.gurke", Confirmed = true });
        rs.Put(new Product { Id = "prod.salat", Name = "Gurkensalat", Recipe = [new() { IngredientId = "ing.gurke", Amount = 200, Unit = "GRM" }] });
        return rs;
    }

    static Case Kase() => new()
    {
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Invoices = [new Invoice { Id = "inv.1", Number = "R-1", Date = new DateOnly(2024, 5, 2), Lines = [new() { No = 1, Name = "Salatgurke", Quantity = 5_000, UnitCode = "H87", LineNet = 400 }] }],
        Products = [new() { ProductId = "prod.salat", GrossPrice = 535, Vat = 700 }],
    };

    [Fact]
    public void APieceIsWeighedLiveAndMarkedAsEstimated()
    {
        var r = Calculation.Run(Kase(), Rules(new Piece(400, Unit.G)));

        var gurke = Assert.Single(r.Ingredients);
        var p = Assert.Single(gurke.Purchases);
        Assert.Equal((400L, FactorSource.Piece, 2_000L), (p.Factor, p.Source, p.Qty));
        Assert.True(gurke.Estimated);
        Assert.Empty(r.Unmapped);
        var flag = Assert.Single(r.Warnings, w => w.Code == "piece_weight");
        Assert.Contains("1 Stk Gurken ≈ 400 g (Richtwert der Zutat)", flag.Message);
        Assert.Contains("<sup>≈</sup>", Html.Render(Kase(), Rules(new Piece(400, Unit.G)), r, null));
    }

    [Fact]
    public void ACorrectedWeightReachesEveryLineWithoutTouchingTheMapping()
    {
        var rs = Rules(new Piece(300, Unit.G));
        Assert.Null(rs.Mappings["map.gurke"].Factor);
        Assert.Equal(1_500, Calculation.Run(Kase(), rs).Ingredients[0].Bought);
    }

    [Fact]
    public void WithoutAWeightThePieceStaysOpen()
    {
        var r = Calculation.Run(Kase(), Rules(null));
        Assert.Single(r.Unmapped);
        Assert.Contains(r.Warnings, w => w.Code == "missing_factor");
        Assert.DoesNotContain(r.Warnings, w => w.Code == "piece_weight");
    }

    [Fact]
    public void AHumansFactorIsNoEstimate()
    {
        var rs = Rules(new Piece(400, Unit.G));
        rs.Mappings["map.gurke"].Factor = 350;
        var p = Calculation.Run(Kase(), rs).Ingredients[0].Purchases[0];
        Assert.Equal((350L, FactorSource.Manual, 1_750L), (p.Factor, p.Source, p.Qty));
    }

    [Fact]
    public void AWeightIsCountedInPiecesOnlyOnceForTheLine()
    {
        var rs = Rules(new Piece(400, Unit.G));
        rs.Products["prod.salat"].Recipe[0].Unit = "H87";
        rs.Products["prod.salat"].Recipe[0].Amount = 1;
        var c = Kase();
        c.Invoices[0].Lines[0].UnitCode = "KGM";
        c.Invoices[0].Lines[0].Quantity = 10_000;
        var p = Calculation.Run(c, rs).Ingredients[0].Purchases[0];
        Assert.Equal((1_000L, 400L, FactorSource.Piece, 25L), (p.Factor, p.Per, p.Source, p.Qty));
    }

    [Fact]
    public void TheUnitTableStillNeedsNoFactor()
    {
        var c = Kase();
        c.Invoices[0].Lines[0].UnitCode = "KGM";
        var p = Calculation.Run(c, Rules(new Piece(400, Unit.G))).Ingredients[0].Purchases[0];
        Assert.Equal((0L, FactorSource.Table, 5_000L), (p.Factor, p.Source, p.Qty));
    }
}

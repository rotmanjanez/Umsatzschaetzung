using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Calc;

// A pin on the whole calculation over the bar case, independent of the service.
public class CalculationTests
{
    static readonly Case Kase = Vorlage.Load();
    static readonly RuleSet Rules = TestData.Seed();
    static readonly Report Report = Calculation.Run(Kase, Rules);

    [Fact]
    public void TheTotalsAddUp()
    {
        var t = Report.Totals;
        Assert.Equal(169_000, t.Purchases);
        Assert.Equal(151_310, t.CostOfGoods);
        Assert.Equal(17_690, t.StockChange);
        Assert.Equal(733_595, t.CalculatedRevenueNet);
        Assert.Equal(40_456, t.Markup);
        Assert.Equal(5_915, t.ShrinkageCost);
        Assert.Equal(4, t.UnallocatedCost);
        Assert.Equal(145_391, t.AllocatedCost);
        Assert.Equal(2_946, t.Portions);
        Assert.Equal(t.Purchases, t.CostOfGoods + t.StockChange);
        Assert.Equal(t.CostOfGoods, t.ShrinkageCost + t.UnallocatedCost + t.AllocatedCost);
    }

    [Fact]
    public void NothingIsExcluded()
    {
        Assert.Empty(Report.Unmapped);
        Assert.Empty(Report.Unused);
        Assert.Equal(0, Report.Totals.ExcludedShare);
    }

    [Fact]
    public void DepositsStayOutOfThePurchases()
    {
        var kase = Vorlage.Load();
        var rules = TestData.Seed();
        rules.Ingredients[Normalize.Deposit] = new Ingredient { Id = Normalize.Deposit, Name = "Pfand und Leergut", CategoryId = "cat.kein.wareneinsatz" };
        rules.Mappings["map.pfand"] = new ArticleMapping { Id = "map.pfand", IngredientId = Normalize.Deposit, Confirmed = true };
        var lines = kase.Invoices[0].Lines;
        var no = lines.Max(l => l.No);
        lines.Add(new InvoiceLine { No = no + 1, Name = "Pfand Fass KEG", Quantity = 2000, UnitCode = "H87", UnitPrice = 30_000_000, PriceBaseQty = 1000, LineNet = 6_000, MappingId = "map.pfand" });
        lines.Add(new InvoiceLine { No = no + 2, Name = "Leergut Fass KEG", Quantity = -1000, UnitCode = "H87", UnitPrice = 30_000_000, PriceBaseQty = 1000, LineNet = -3_000, MappingId = "map.pfand" });
        var r = Calculation.Run(kase, rules);
        Assert.Equal(2, r.Deposits.Count);
        Assert.Empty(r.Unused);
        Assert.Equal(6_000, r.Totals.DepositCharged);
        Assert.Equal(-3_000, r.Totals.DepositRefunded);
        Assert.Equal(Report.Totals.Purchases, r.Totals.Purchases);
        Assert.Equal(0, r.Totals.ExcludedShare);
        Assert.DoesNotContain(Included.Of(kase, r).SelectMany(i => i.Lines), l => l.No > no);
    }

    [Fact]
    public void TheDrinksDivisionCarriesTheWholeMarkup()
    {
        var drinks = Assert.Single(Report.Markups);
        Assert.Equal(Sparte.Getränke, drinks.Sparte);
        Assert.Equal(145_391, drinks.CostOfGoods);
        Assert.Equal(733_595, drinks.RevenueNet);
        Assert.Equal(40_456, drinks.Markup);
    }

    [Fact]
    public void EachRecipeCarriesItsOwnMarkup()
    {
        var beer = Report.Products.Single(p => p.ProductId == "prod.pils.03");
        Assert.Equal("Pils 0,3 l vom Fass", Names.Product(Rules, beer.ProductId));
        Assert.Equal(Sparte.Getränke, beer.Sparte);
        Assert.Equal(55, beer.CostPerPortion);
        Assert.Equal(38_288, beer.Markup);
        Assert.Equal(Report.Totals.CalculatedRevenueNet, Report.Products.Sum(p => p.RevenueNet));
        Assert.Equal(Report.Totals.Portions, Report.Products.Sum(p => p.Portions));
    }

    [Fact]
    public void EachIngredientCarriesItsPurchasesYieldAndStock()
    {
        var pils = Report.Ingredients.Single(i => i.Name == "Fassbier Pils");
        var purchase = Assert.Single(pils.Purchases);
        Assert.Equal(("inv.bar.1", "2024-04711", 1L), (purchase.InvoiceId, purchase.Invoice, purchase.LineNo));
        Assert.Equal(111_000, purchase.Net);
        Assert.Equal(pils.Purchases.Sum(p => p.Qty), pils.Bought);
        Assert.Equal(pils.Purchases.Sum(p => p.Net), pils.Cost);
        Assert.Equal(pils.Opening + pils.Bought - pils.Closing, pils.Used);
        Assert.False(string.IsNullOrEmpty(pils.Yield?.Name));
        Assert.True(pils.YieldRate is > 0 and < Bp.Full);
        Assert.Equal(pils.Used * pils.YieldRate / Bp.Full, pils.Sellable);
    }

    [Fact]
    public void TheCalculationIsRepeatable()
    {
        var again = Calculation.Run(Vorlage.Load(), TestData.Seed());
        Assert.Equal(Report.Totals.CalculatedRevenueNet, again.Totals.CalculatedRevenueNet);
        Assert.Equal(Report.Products.Select(p => (p.ProductId, p.Portions)), again.Products.Select(p => (p.ProductId, p.Portions)));
    }
}

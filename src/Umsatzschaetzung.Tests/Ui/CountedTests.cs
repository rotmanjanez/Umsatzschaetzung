using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

// Cups are packaging, in no Sparte: counted only once a product sold reaches them, and then the
// mapping screen asks for the factor exactly where the calculation misses it.
public class CountedTests
{
    static (Case, RuleSet) Cups(bool sold, long? factor)
    {
        var kase = Vorlage.Load();
        var rules = TestData.Seed();
        rules.Categories["cat.kein"] = new Category { Id = "cat.kein", Name = "Kein Wareneinsatz" };
        rules.Products["prod.verpackung"] = new Product { Id = "prod.verpackung", Name = "Verpackung und Einweg", Unit = "H87", CategoryId = "cat.kein" };
        rules.Products["prod.kaffee.to.go"] = new Product
        {
            Id = "prod.kaffee.to.go", Name = "Kaffee to go", Unit = "H87", Recipe = [new() { PartId = "prod.verpackung", Amount = 1, Unit = "H87" }],
        };
        rules.Mappings["map.becher"] = new ArticleMapping { Id = "map.becher", ProductId = "prod.verpackung", Factor = factor, Confirmed = true };
        var lines = kase.Invoices[0].Lines;
        lines.Add(new InvoiceLine
        {
            No = lines.Max(l => l.No) + 1, Name = "Coffee-to-go-Becher, Karton", Quantity = 1000, UnitCode = "XCT",
            UnitPrice = 45_000_000, PriceBaseQty = 1000, LineNet = 4_500, MappingId = "map.becher",
        });
        if (sold) kase.Products.Add(new CaseProduct { ProductId = "prod.kaffee.to.go", GrossPrice = 350, Vat = 1900 });
        return (kase, rules);
    }

    static (bool Asked, bool Missing) Both(bool sold, long? factor)
    {
        var (kase, rules) = Cups(sold, factor);
        var asked = LineGroup.Of(kase, rules).Single(g => g.Name.StartsWith("Coffee", StringComparison.Ordinal)).State == Checked.Pending;
        var missing = Calculation.Run(kase, rules).Warnings.Exists(w => w.Code == "missing_factor");
        return (asked, missing);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, 1000L, false)]
    [InlineData(false, null, false)]
    public void TheMappingScreenAsksForAFactorWhereTheCalculationMissesOne(bool sold, long? factor, bool expected) =>
        Assert.Equal((expected, expected), Both(sold, factor));
}

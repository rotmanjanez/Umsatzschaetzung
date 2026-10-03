using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Calc;

public class SuggestionsTests
{
    [Fact]
    public void OnlyAWareNothingIsMadeFromAndInASparteIsOfferedAsBought()
    {
        var kase = Vorlage.Load();
        var rules = TestData.Seed();
        rules.Categories["cat.kein"] = new Category { Id = "cat.kein", Name = "Kein Wareneinsatz" };
        rules.Categories["cat.tabak"] = new Category { Id = "cat.tabak", Name = "Tabakwaren", Sparte = Sparte.Handelsware };
        rules.Products["prod.zigarre"] = new Product { Id = "prod.zigarre", Name = "Zigarre", Unit = "H87", Recipe = [new() { PartId = "prod.tabak", Amount = 1, Unit = "H87" }] };
        var lines = kase.Invoices[0].Lines;
        var no = lines.Max(l => l.No);
        foreach (var (id, category) in new[] { ("prod.reinigung", "cat.kein"), ("prod.zigaretten", "cat.tabak"), ("prod.tabak", "cat.tabak") })
        {
            rules.Products[id] = new Product { Id = id, Name = id, Unit = "H87", CategoryId = category };
            rules.Mappings["map." + id] = new ArticleMapping { Id = "map." + id, ProductId = id, Confirmed = true };
            lines.Add(new InvoiceLine { No = ++no, Name = id, Quantity = 10_000, UnitCode = "H87", UnitPrice = 5_000_000, PriceBaseQty = 1000, LineNet = 50_000, MappingId = "map." + id });
        }

        var offered = Suggestions.For(kase, rules, new HashSet<string>());

        Assert.Contains("prod.zigaretten", offered);
        Assert.DoesNotContain("prod.reinigung", offered);
        Assert.DoesNotContain("prod.tabak", offered);
    }
}

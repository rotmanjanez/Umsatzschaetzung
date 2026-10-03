using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;

namespace Umsatzschaetzung.Tests.Calc;

// Gekauft und verkauft wird an jedem Produkt des Baums: Pommes kommen tiefgekühlt oder aus Kartoffeln.
public class TreeTests
{
    const string Smp = "prod.smp", Pommes = "prod.pommes", Schnitzel = "prod.schnitzel";

    static PartLine Line(string partId, long amount, string unit) => new() { PartId = partId, Amount = amount, Unit = unit };

    static RuleSet Rules()
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "cat.fleisch", Name = "Fleisch", Sparte = Sparte.Speisen });
        rs.Put(new Category { Id = "cat.tk", Name = "Tiefkühlware", Sparte = Sparte.Handelsware });
        rs.Put(new Product { Id = "prod.schwein", Name = "Schweinefleisch", Unit = "GRM", CategoryId = "cat.fleisch" });
        rs.Put(new Product { Id = "prod.kartoffeln", Name = "Kartoffeln", Unit = "GRM" });
        rs.Put(new Product { Id = "prod.oel", Name = "Öl", Unit = "MLT" });
        rs.Put(new Product { Id = Schnitzel, Name = "Schnitzel", Unit = "H87", Recipe = [Line("prod.schwein", 180, "GRM")] });
        rs.Put(new Product { Id = Pommes, Name = "Pommes", Unit = "H87", Recipe = [Line("prod.kartoffeln", 250, "GRM"), Line("prod.oel", 20, "MLT")] });
        rs.Put(new Product { Id = Smp, Name = "Schnitzel mit Pommes", Unit = "H87", Recipe = [Line(Schnitzel, 1, "H87"), Line(Pommes, 1, "H87")] });
        foreach (var (id, name) in new[] { ("prod.schwein", "Schweineschnitzel"), ("prod.kartoffeln", "Kartoffeln"), ("prod.oel", "Rapsöl"), (Pommes, "TK Pommes Portion") })
            rs.Put(new ArticleMapping { Id = "map." + id, Name = name, ProductId = id, Confirmed = true });
        return rs;
    }

    static InvoiceLine Bought(long no, string name, long quantity, string unit, long net, string productId) =>
        new() { No = no, Name = name, Quantity = quantity, UnitCode = unit, LineNet = net, MappingId = "map." + productId };

    // 40 Portionen Pommes gekauft, Kartoffeln für 60, Öl für 100, Fleisch für 100 Schnitzel.
    static Case Kase(params CaseProduct[] sold) => new()
    {
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Invoices = [new Invoice
        {
            Id = "inv.1", Number = "R-1", Date = new DateOnly(2024, 5, 2), Lines =
            [
                Bought(1, "Schweineschnitzel", 18_000, "KGM", 18_000, "prod.schwein"),
                Bought(2, "Kartoffeln", 15_000, "KGM", 1_500, "prod.kartoffeln"),
                Bought(3, "Rapsöl", 2_000, "LTR", 600, "prod.oel"),
                Bought(4, "TK Pommes Portion", 40_000, "H87", 2_000, Pommes),
            ],
        }],
        Products = [.. sold],
    };

    static CaseProduct Sold(string id, long gross = 1590) => new() { ProductId = id, GrossPrice = gross, Vat = 1900 };

    static ProductRow Row(Report r, string id) => r.Products.Single(p => p.ProductId == id);

    static SupplyRow Supply(Report r, string id) => r.Supply.Single(s => s.ProductId == id);

    [Fact]
    public void BoughtAndHomemadePommesBothGoIntoTheDish()
    {
        var r = Calculation.Run(Kase(Sold(Smp)), Rules());

        var smp = Row(r, Smp);
        Assert.Equal(100, smp.Portions);
        Assert.Equal([40L, 60L], smp.Routes.Select(w => w.Portions));
        Assert.Equal([(Pommes, 1L)], smp.Routes[0].Parts.Where(p => p.ProductId != "prod.schwein").Select(p => (p.ProductId, p.PerPortion)));
        Assert.Equal([("prod.kartoffeln", 250L), ("prod.oel", 20L)], smp.Routes[1].Parts.Where(p => p.ProductId != "prod.schwein").Select(p => (p.ProductId, p.PerPortion)));
        Assert.Equal(Sparte.Speisen, smp.Sparte);
        Assert.Equal((40L, 0L), (Supply(r, Pommes).UsedBy.Single().Portions, Supply(r, Pommes).Leftover));
        Assert.Equal((60L, 250L), (Supply(r, "prod.kartoffeln").UsedBy.Single().Portions, Supply(r, "prod.kartoffeln").UsedBy.Single().PerPortion));
        Assert.Equal((100L, 180L), (Supply(r, "prod.schwein").UsedBy.Single().Portions, Supply(r, "prod.schwein").UsedBy.Single().PerPortion));
        Assert.Equal(smp.CostOfGoods, r.Totals.AllocatedCost);
        Assert.Empty(r.Unused);
    }

    [Fact]
    public void TheReportShowsWhereThePortionsCameFrom()
    {
        var c = Kase(Sold(Smp));
        var html = Html.Render(c, Rules(), Calculation.Run(c, Rules()), null);

        Assert.Contains("<h2>Portionen je Herkunft</h2>", html);
        Assert.Contains("<span>1 Stück Pommes</span>", html);
        Assert.Contains("<span>250 g Kartoffeln</span>", html);
    }

    // 1 ct je g Fleisch, 50 ct je gekaufte Portion, 0,1 ct je g Kartoffeln, 0,3 ct je ml Öl.
    [Fact]
    public void EachRouteCarriesItsOwnCost()
    {
        var r = Calculation.Run(Kase(Sold(Smp)), Rules());

        var smp = Row(r, Smp);
        Assert.Equal([230L, 211L], smp.Routes.Select(w => w.CostPerPortion));
        Assert.Equal(40 * 230 + 60 * 211, smp.CostOfGoods);
        Assert.Equal(218, smp.CostPerPortion);
    }

    [Fact]
    public void AYieldOnABoughtInnerProductShrinksOnlyItsRoute()
    {
        var rs = Rules();
        rs.Put(new YieldRule { Id = "yield.pommes", Name = "Bruch", ProductId = Pommes, Deduction = 5_000, Default = true });

        var r = Calculation.Run(Kase(Sold(Smp)), rs);

        Assert.Equal(20, Supply(r, Pommes).Sellable);
        Assert.Equal([20L, 60L], Row(r, Smp).Routes.Select(w => w.Portions));
    }

    [Fact]
    public void StockAloneSuppliesAnInnerProduct()
    {
        var c = Kase(Sold(Smp));
        c.Invoices[0].Lines.RemoveAt(3);
        c.Inventory = [new InventoryEntry { ProductId = Pommes, Opening = 30, Closing = 0, Unit = "H87" }];

        var r = Calculation.Run(c, Rules());

        Assert.Equal((0L, 30L, 30L), (Supply(r, Pommes).Bought, Supply(r, Pommes).Used, Supply(r, Pommes).Leftover + Supply(r, Pommes).UsedBy.Sum(u => u.Qty)));
        Assert.Equal([30L, 60L], Row(r, Smp).Routes.Select(w => w.Portions));
    }

    [Fact]
    public void PinsOnProductsSharingASupplyTakeItInTheirOrder()
    {
        var c = Kase(Sold(Smp), Sold(Pommes, 450));
        c.Pinned = [new PinnedPortions { ProductId = Smp, Portions = 50 }, new PinnedPortions { ProductId = Pommes, Portions = 30 }];

        var r = Calculation.Run(c, Rules());

        Assert.Equal((30L, 50L), (Row(r, Pommes).Portions, Row(r, Smp).Portions));
        Assert.Empty(Row(r, Pommes).Routes);
        Assert.Equal([10L, 40L], Row(r, Smp).Routes.Select(w => w.Portions));
        Assert.Equal(0, Supply(r, Pommes).Leftover);
        Assert.DoesNotContain(r.Warnings, w => w.Code == "pinned-overdrawn");
    }

    [Fact]
    public void APartNobodyBoughtKeepsTheAllocationExact()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.x", Name = "X", Unit = "GRM" });
        rs.Put(new Product { Id = "prod.y", Name = "Y", Unit = "GRM" });
        rs.Put(new Product { Id = "prod.a", Name = "A", Unit = "H87", Recipe = [Line("prod.x", 10, "GRM"), Line("prod.y", 10, "GRM")] });
        rs.Put(new Product { Id = "prod.b", Name = "B", Unit = "H87", Recipe = [Line("prod.x", 10, "GRM"), Line("prod.y", 20, "GRM")] });
        rs.Put(new ArticleMapping { Id = "map.prod.y", Name = "Y", ProductId = "prod.y", Confirmed = true });
        var c = Kase(Sold("prod.a"), Sold("prod.b"));
        c.Invoices[0].Lines = [Bought(1, "Y", 1_000, "KGM", 1_000, "prod.y")];

        var r = Calculation.Run(c, rs);

        Assert.False(Assert.Single(r.Allocations).Approximate);
        Assert.Equal(1_000, Supply(r, "prod.y").Leftover);
        Assert.DoesNotContain(r.Warnings, w => w.Code == "approximate");
    }

    [Fact]
    public void PommesSoldAloneAndInTheDishShareBothSupplies()
    {
        var c = Kase(Sold(Smp), Sold(Pommes, 450));
        var lines = c.Invoices[0].Lines;
        (lines[0].Quantity, lines[1].Quantity, lines[2].Quantity, lines[3].Quantity) = (1_800, 3_500, 280, 4_000);

        var r = Calculation.Run(c, Rules());

        Assert.Equal((10L, 8L), (Row(r, Smp).Portions, Row(r, Pommes).Portions));
        Assert.All(r.Supply, s => Assert.Equal(0, s.Leftover));
        Assert.All(r.Supply, s => Assert.Equal(s.Sellable, s.UsedBy.Sum(u => u.Qty) + s.Leftover));
    }

    [Fact]
    public void ALeafIsSoldStraightFromItsSupply()
    {
        var r = Calculation.Run(Kase(Sold("prod.kartoffeln", 1)), Rules());

        var row = Row(r, "prod.kartoffeln");
        Assert.Equal(15_000, row.Portions);
        Assert.Empty(row.Routes);
        Assert.Equal(["Rapsöl", "Schweineschnitzel", "TK Pommes Portion"], r.Unused.Select(l => l.Name).Order());
    }

    [Fact]
    public void SupplyNoSoldProductReachesIsUnused()
    {
        var r = Calculation.Run(Kase(Sold(Schnitzel)), Rules());

        Assert.Equal(100, Row(r, Schnitzel).Portions);
        Assert.Equal(["prod.schwein"], r.Supply.Select(s => s.ProductId));
        Assert.Equal(["prod.kartoffeln", "prod.oel", Pommes], r.Unused.Select(l => l.ProductId).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AnInnerProductsCaseRecipeReachesItsParents()
    {
        var schnitzel = Sold(Schnitzel);
        schnitzel.Recipe = [Line("prod.schwein", 200, "GRM")];
        var c = Kase(Sold(Smp), schnitzel);
        c.Pinned = [new PinnedPortions { ProductId = Schnitzel, Portions = 0 }];

        var r = Calculation.Run(c, Rules());

        Assert.Equal(90, Row(r, Smp).Portions);
        Assert.Equal(200, Supply(r, "prod.schwein").UsedBy.Single().PerPortion);
        Assert.Equal(180, Assert.Single(Rules().Products[Schnitzel].Recipe).Amount);
    }

    [Fact]
    public void AnUneditedCaseCopyOfTheRecipeKeepsTheBoughtInnerProduct()
    {
        var rs = Rules();
        var smp = Sold(Smp);
        smp.Recipe = [.. rs.Products[Smp].Recipe];
        smp.RecipeBasis = Recipes.Basis(rs.Products[Smp]);

        var r = Calculation.Run(Kase(smp), rs);

        Assert.Equal([Schnitzel, Pommes], smp.Recipe.Select(l => l.PartId));
        Assert.False(Recipes.Stale(smp, rs));
        Assert.Equal([40L, 60L], Row(r, Smp).Routes.Select(w => w.Portions));
        Assert.Empty(r.Unused);
    }

    [Fact]
    public void APinFillsTheOwnSupplyFirstAndTheRestHomemade()
    {
        var c = Kase(Sold(Smp));
        c.Pinned = [new PinnedPortions { ProductId = Smp, Portions = 70 }];

        var r = Calculation.Run(c, Rules());

        var smp = Row(r, Smp);
        Assert.True(smp.Pinned);
        Assert.Equal(70, smp.Portions);
        Assert.Equal([40L, 30L], smp.Routes.Select(w => w.Portions));
        Assert.DoesNotContain(r.Warnings, w => w.Code == "pinned-overdrawn");
    }

    [Fact]
    public void APinBeyondEveryRouteIsFlagged()
    {
        var c = Kase(Sold(Smp));
        c.Pinned = [new PinnedPortions { ProductId = Smp, Portions = 120 }];

        var r = Calculation.Run(c, Rules());

        Assert.Equal([40L, 80L], Row(r, Smp).Routes.Select(w => w.Portions));
        Assert.Contains("„Schnitzel mit Pommes“", Assert.Single(r.Warnings, w => w.Code == "pinned-overdrawn").Message);
    }

    [Fact]
    public void ACycleThroughACaseRecipeIsRefused()
    {
        var schnitzel = Sold(Schnitzel);
        schnitzel.Recipe = [Line(Smp, 1, "H87")];

        Assert.Throws<InvalidOperationException>(() => Calculation.Run(Kase(Sold(Smp), schnitzel), Rules()));
    }

    [Fact]
    public void TooManyRoutesKeepTheOwnSupplyAndMarkTheAllocation()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        List<PartLine> parts = [];
        List<InvoiceLine> lines = [new() { No = 1, Name = "Mehl", Quantity = 10_000, UnitCode = "KGM", LineNet = 1_000, MappingId = "map.mehl" }];
        rs.Put(new ArticleMapping { Id = "map.mehl", Name = "Mehl", ProductId = "prod.mehl", Confirmed = true });
        for (var i = 1; i <= 7; i++)
        {
            var id = "prod.teil" + i;
            rs.Put(new Product { Id = id, Name = "Teil " + i, Unit = "H87", Recipe = [Line("prod.mehl", 10, "GRM")] });
            rs.Put(new ArticleMapping { Id = "map." + id, Name = "Teil " + i, ProductId = id, Confirmed = true });
            lines.Add(new() { No = i + 1, Name = "Teil " + i, Quantity = 5_000, UnitCode = "H87", LineNet = 500, MappingId = "map." + id });
            parts.Add(Line(id, 1, "H87"));
        }
        rs.Put(new Product { Id = "prod.platte", Name = "Platte", Unit = "H87", Recipe = parts });
        var stocked = new HashSet<string>(rs.Products.Keys.Where(k => k != "prod.platte"));

        var (routes, cut) = Routes.Of(rs, rs.Products["prod.platte"], stocked);

        Assert.True(cut);
        Assert.Equal(Routes.Cap, routes.Count);
        Assert.Equal(parts.Select(p => p.PartId), routes[0].Keys);
        Assert.Equal(["prod.mehl"], routes[^1].Keys);
        Assert.All(parts, p =>
        {
            Assert.Contains(routes, w => w.ContainsKey(p.PartId) && w.Count > 1);
            Assert.Contains(routes, w => !w.ContainsKey(p.PartId) && w.Count > 1);
        });
        var c = new Case
        {
            PeriodFrom = new DateOnly(2024, 1, 1),
            PeriodTo = new DateOnly(2024, 12, 31),
            Invoices = [new Invoice { Id = "inv.1", Number = "R-1", Date = new DateOnly(2024, 5, 2), Lines = lines }],
            Products = [Sold("prod.platte")],
        };
        var r = Calculation.Run(c, rs);
        Assert.True(Assert.Single(r.Allocations).Approximate);
        Assert.Contains("„Platte“", Assert.Single(r.Warnings, w => w.Code == "approximate").Message);
        Assert.Equal(147, Row(r, "prod.platte").Portions);
    }

    // 9 kg Mehl in einem Ansatz für 20 Brote sind 450 g je Brot, nicht 0 kg.
    [Fact]
    public void ABatchIsScaledInBaseUnitsAndRoundedOnce()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        rs.Put(new Product { Id = "prod.wasser", Name = "Wasser", Unit = "MLT" });
        rs.Put(new Product { Id = "prod.brot", Name = "Brot", Unit = "H87", Batch = 20, Recipe = [Line("prod.mehl", 9, "KGM"), Line("prod.wasser", 6, "LTR")] });
        rs.Put(new Product { Id = "prod.teig", Name = "Teig", Unit = "GRM", Batch = 20_000, Recipe = [Line("prod.mehl", 12, "KGM"), Line("prod.wasser", 7, "LTR")] });
        rs.Put(new Product { Id = "prod.pizza", Name = "Pizza", Unit = "H87", Recipe = [Line("prod.teig", 250, "GRM")] });

        Assert.Equal([("prod.mehl", 450L, "GRM"), ("prod.wasser", 300L, "MLT")], Recipes.Sold(rs, rs.Products["prod.brot"]).Select(l => (l.PartId, l.Amount, l.Unit)));
        Assert.Equal([("prod.mehl", 150L, "GRM"), ("prod.wasser", 88L, "MLT")], Recipes.Sold(rs, rs.Products["prod.pizza"]).Select(l => (l.PartId, l.Amount, l.Unit)));
        var (routes, _) = Routes.Of(rs, rs.Products["prod.pizza"], new HashSet<string> { "prod.mehl", "prod.wasser" });
        Assert.Equal([("prod.mehl", 150L), ("prod.wasser", 88L)], Assert.Single(routes).Select(a => (a.Key, a.Value)));
    }

    [Fact]
    public void TheReportNamesWhatABatchMakes()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        rs.Put(new ArticleMapping { Id = "map.prod.mehl", Name = "Mehl", ProductId = "prod.mehl", Confirmed = true });
        rs.Put(new Product { Id = "prod.brot", Name = "Brot", Unit = "H87", Batch = 20, Recipe = [Line("prod.mehl", 9, "KGM")] });
        var c = Kase(Sold("prod.brot", 300));
        c.Invoices[0].Lines = [Bought(1, "Mehl", 9_000, "KGM", 900, "prod.mehl")];

        var r = Calculation.Run(c, rs);
        var html = Html.Render(c, rs, r, null);

        Assert.Equal((20L, 45L), (r.Products.Single().Portions, r.Products.Single().CostPerPortion));
        Assert.Contains("<td class=\"list\"><span>9 Kilogramm Mehl</span><span>ergibt 20 Stück</span></td>\n            <td>Übrige</td>\n            <td class=\"num\">0,45 €</td>", html);
    }

    [Fact]
    public void APositiveAmountNeverRoundsToNothing()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.ei", Name = "Ei", Unit = "H87" });
        rs.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        rs.Put(new Product { Id = "prod.kuchen", Name = "Kuchenstück", Unit = "H87", Batch = 12, Recipe = [Line("prod.ei", 1, "H87"), Line("prod.mehl", 1, "GRM")] });

        Assert.Equal([("prod.ei", 1L), ("prod.mehl", 1L)], Recipes.Sold(rs, rs.Products["prod.kuchen"]).Select(l => (l.PartId, l.Amount)));
    }

    [Fact]
    public void ARouteAddsUpOnePartReachedTwiceBeforeRounding()
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.mehl", Name = "Mehl", Unit = "GRM" });
        rs.Put(new Product { Id = "prod.a", Name = "A", Unit = "H87", Batch = 3, Recipe = [Line("prod.mehl", 1, "GRM")] });
        rs.Put(new Product { Id = "prod.paar", Name = "Paar", Unit = "H87", Recipe = [Line("prod.a", 1, "H87"), Line("prod.a", 1, "H87")] });

        Assert.Equal([("prod.mehl", 1L)], Recipes.Sold(rs, rs.Products["prod.paar"]).Select(l => (l.PartId, l.Amount)));
        Assert.Equal(1, Assert.Single(Routes.Of(rs, rs.Products["prod.paar"], new HashSet<string> { "prod.mehl" }).Routes)["prod.mehl"]);
    }
}

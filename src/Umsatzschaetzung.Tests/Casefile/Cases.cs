using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Rulestore;

namespace Umsatzschaetzung.Tests.Casefile;

static class Cases
{
    public const string FixtureId = "case.bar.2024";

    public static Case Minimal(string id, string label = "Prüfung") => new()
    {
        Id = id,
        Label = label,
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Taxpayer = new Taxpayer { Name = "Muster", TaxNumber = "123/4567", PabNumber = "89" },
        CreatedAt = new DateTimeOffset(2024, 5, 2, 8, 0, 0, TimeSpan.Zero),
        UpdatedAt = new DateTimeOffset(2024, 5, 3, 9, 30, 15, TimeSpan.Zero),
    };

    public static Case Full(string id)
    {
        var c = Minimal(id, "Schankwirtschaft „Zum Fass“ – Bp 2024 🍺");
        c.Taxpayer = new Taxpayer { Name = "Zum Fass GmbH", TaxNumber = "214/5711/0832", PabNumber = "PaB 2024/0417", Gewerbe = "56101.0" };
        c.CreatedAt = new DateTimeOffset(2024, 5, 2, 8, 0, 0, 123, TimeSpan.FromHours(2));
        c.UpdatedAt = new DateTimeOffset(2025, 1, 31, 23, 59, 59, TimeSpan.FromHours(-5));
        c.Declared = [new() { Vat = 1900, Net = 733595 }, new() { Vat = 0, Net = 0 }, new() { Vat = 700, Net = 12 }];
        c.Inventory =
        [
            new() { IngredientId = "ing.bier.fass", Opening = 50_000, Closing = 100_000, Unit = "LTR" },
            new() { IngredientId = "ing.korn", Opening = 0, Closing = -1, Unit = "" },
            new() { IngredientId = "ing.bier.fass", Opening = 1, Closing = 2, Unit = "" },
        ];
        c.Products =
        [
            new() { ProductId = "prod.pils.05", GrossPrice = 450, Vat = 1900 },
            new() { ProductId = "prod.pils.03", GrossPrice = 0, Vat = 700, Disabled = true },
        ];
        c.Yields =
        [
            new() { IngredientId = "ing.bier.fass", YieldRuleId = "yr.b" },
            new() { CategoryId = "cat.bier", YieldRuleId = "yr.a" },
        ];
        c.Pinned = [new() { ProductId = "prod.pils.05", Portions = 1200, Reason = "laut Kassenbuch" }, new() { ProductId = "prod.x", Portions = 0, Reason = "" }];
        c.Invoices =
        [
            new()
            {
                Id = "re-2", Source = Source.Scan, FileName = "Scan März.jpg", SupplierName = "Bäckerei Ölmühle", Number = "R/1",
                Currency = "EUR", NetTotal = 100, GrossTotal = 107, StatedNet = null, StatedGross = 0,
                Verification = new() { At = new DateTimeOffset(2024, 6, 1, 12, 0, 0, TimeSpan.FromHours(2)), Auto = true },
            },
            new()
            {
                Id = "re-1", Source = Source.Zugferd, FileName = "rheinland.pdf", SupplierName = "Rheinland", Number = "2024-04711",
                Date = new DateOnly(2024, 3, 15), Currency = "EUR", NetTotal = 169000, GrossTotal = 201110, StatedNet = 169000, StatedGross = 201110,
                Verification = new() { At = new DateTimeOffset(2024, 6, 2, 0, 0, 0, TimeSpan.Zero), Auto = false },
                Lines =
                [
                    new() { No = 2, Name = "Doppelkorn 0,7 l", SellerArticleId = "55120", Gtin = "4001234567890", Quantity = 20000, UnitCode = "XBO",
                        UnitPrice = 9200000, PriceBaseQty = 1000, LineNet = 18400, Vat = 1900, MappingId = "map.korn07" },
                    new() { No = 1, Name = "Pils Fass 50 l", Quantity = -12000, UnitCode = "", UnitPrice = 0, PriceBaseQty = 0, LineNet = -111000, Vat = 0 },
                ],
            },
        ];
        return c;
    }

    // Die Vorlage case.sql legt ihre Zeilen über einen gespeicherten Rahmenfall.
    public static Case Fixture(CaseStore store, string dir)
    {
        store.Save(Minimal(FixtureId, "Vorlage"));
        Sql.Exec(Path.Combine(dir, FixtureId + ".db"), File.ReadAllText(TestData.File("case.sql")));
        return store.Load(FixtureId);
    }

    public static void HoldsFile(CaseStore store, string caseId, string invoiceId, string name, byte[] data)
    {
        var (n, d) = store.LoadFile(caseId, invoiceId);
        Assert.Equal(name, n);
        Assert.Equal(data, d);
    }

    public static void Same(Case expected, Case actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Label, actual.Label);
        Assert.Equal(expected.PeriodFrom, actual.PeriodFrom);
        Assert.Equal(expected.PeriodTo, actual.PeriodTo);
        Assert.Equal(
            (expected.Taxpayer.Name, expected.Taxpayer.TaxNumber, expected.Taxpayer.PabNumber, expected.Taxpayer.Gewerbe),
            (actual.Taxpayer.Name, actual.Taxpayer.TaxNumber, actual.Taxpayer.PabNumber, actual.Taxpayer.Gewerbe));
        Assert.Equal(expected.Declared.Select(d => (d.Vat, d.Net)), actual.Declared.Select(d => (d.Vat, d.Net)));
        Assert.Equal(expected.Inventory.Select(e => (e.IngredientId, e.Opening, e.Closing, e.Unit)),
            actual.Inventory.Select(e => (e.IngredientId, e.Opening, e.Closing, e.Unit)));
        Assert.Equal(expected.Products.Select(p => (p.ProductId, p.GrossPrice, p.Vat, p.Disabled)),
            actual.Products.Select(p => (p.ProductId, p.GrossPrice, p.Vat, p.Disabled)));
        Assert.Equal(expected.Yields.Select(y => (y.IngredientId, y.CategoryId, y.YieldRuleId)),
            actual.Yields.Select(y => (y.IngredientId, y.CategoryId, y.YieldRuleId)));
        Assert.Equal(expected.Pinned.Select(p => (p.ProductId, p.Portions, p.Reason)),
            actual.Pinned.Select(p => (p.ProductId, p.Portions, p.Reason)));
        Assert.Equal(expected.Invoices.Select(Json.Serialize), actual.Invoices.Select(Json.Serialize));
        Assert.Equal((expected.CreatedAt, expected.CreatedAt.Offset), (actual.CreatedAt, actual.CreatedAt.Offset));
        Assert.Equal((expected.UpdatedAt, expected.UpdatedAt.Offset), (actual.UpdatedAt, actual.UpdatedAt.Offset));
    }
}

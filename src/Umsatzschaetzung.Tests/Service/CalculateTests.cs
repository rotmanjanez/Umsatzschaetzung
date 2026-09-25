using System.Text;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class CalculateTests : IDisposable
{
    sealed class Printer : IPdfPrinter
    {
        public string? Html;

        public Task<byte[]> Print(string html, CancellationToken ct)
        {
            Html = html;
            return Task.FromResult(File.ReadAllBytes(TestData.File("zugferd.pdf")));
        }
    }

    readonly Printer printer = new();
    readonly Host host;
    readonly IService svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public CalculateTests()
    {
        host = new Host(printer: printer);
        svc = host.Service;
        host.PutVorlage().GetAwaiter().GetResult();
    }

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task TheBarCaseAddsUp()
    {
        var calc = await svc.Calculate(Vorlage.Id, ct);
        var t = calc.Report.Totals;

        Assert.Equal("1.690,00 €", Format.Cents(t.Purchases));
        Assert.Equal("1.513,10 €", Format.Cents(t.CostOfGoods));
        Assert.Equal("176,90 €", Format.Cents(t.StockChange));
        Assert.Equal("7.353,35 €", Format.Cents(t.CalculatedRevenueNet));
        Assert.Equal("405,76 %", Format.Bp(t.Markup));
        Assert.Equal("59,15 €", Format.Cents(t.ShrinkageCost));
        Assert.Equal("0,04 €", Format.Cents(t.UnallocatedCost));
        Assert.Equal("1.453,91 €", Format.Cents(t.AllocatedCost));
        Assert.Equal("2.946 Portionen", Format.Portions(t.Portions));
        Assert.Empty(calc.Report.Unmapped);
        Assert.Empty(calc.Report.Unused);
        Assert.Null(calc.Rahmen);
    }

    [Fact]
    public async Task TheDrinksDivisionCarriesTheWholeMarkup()
    {
        var drinks = (await svc.Calculate(Vorlage.Id, ct)).Report.Markups.Single(m => m.Sparte == Sparte.Getränke);
        Assert.Equal("1.453,91 €", Format.Cents(drinks.CostOfGoods));
        Assert.Equal("7.353,35 €", Format.Cents(drinks.RevenueNet));
        Assert.Equal("405,76 %", Format.Bp(drinks.Markup));
    }

    [Fact]
    public async Task EachRecipeCarriesItsOwnMarkup()
    {
        var rules = await svc.Rules(ct);
        var beer = (await svc.Calculate(Vorlage.Id, ct)).Report.Products.Single(p => Names.Product(rules, p.ProductId) == "Pils 0,3 l vom Fass");
        Assert.Equal(Sparte.Getränke, beer.Sparte);
        Assert.Equal("0,55 €", Format.Cents(beer.CostPerPortion));
        Assert.Equal("384,68 %", Format.Bp(beer.Markup));
    }

    [Fact]
    public async Task EachIngredientCarriesItsPurchasesYieldAndStock()
    {
        var kase = await svc.GetCase(Vorlage.Id, ct);
        var pils = (await svc.Calculate(Vorlage.Id, ct)).Report.Ingredients.Single(i => i.Name == "Fassbier Pils");

        Assert.NotEmpty(pils.Purchases);
        Assert.All(pils.Purchases, p =>
        {
            Assert.Equal(Names.Invoice(kase, p.InvoiceId), p.Invoice);
            Assert.True(p.Qty > 0);
        });
        Assert.Equal(pils.Purchases.Sum(p => p.Qty), pils.Bought);
        Assert.Equal(pils.Purchases.Sum(p => p.Net), pils.Cost);
        Assert.False(string.IsNullOrEmpty(pils.Yield?.Name));
        Assert.True(pils.YieldRate < Bp.Full);
        Assert.Equal(pils.Used * pils.YieldRate / Bp.Full, pils.Sellable);
        Assert.Equal(Format.Qty(pils.Opening + pils.Bought - pils.Closing, pils.Unit), Format.Qty(pils.Used, pils.Unit));
    }

    [Fact]
    public async Task TheReportCarriesTheMarkupSection()
    {
        var report = await svc.RenderReport(Vorlage.Id, false, ct);

        Assert.Contains("7.353,35 €", report.Html);
        Assert.Contains("Anhang D", report.Html);
        Assert.Contains("<h1>2 Rohgewinnaufschlag</h1>", report.Html);
        Assert.Contains("405,76 %", report.Html);
        Assert.Null(report.Pdf);
        Assert.Null(report.FileName);
        Assert.Null(printer.Html);
    }

    [Fact]
    public async Task AReportPrintsWhereAPrinterIsAvailable()
    {
        var report = await svc.RenderReport(Vorlage.Id, true, ct);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(report.Pdf!, 0, 5));
        Assert.Equal(report.Html, printer.Html);
        Assert.EndsWith(".pdf", report.FileName);
    }

    [Fact]
    public async Task AReportDoesNotPrintWithoutAPrinter()
    {
        using var bare = new Host();
        await bare.PutVorlage();
        var e = await Assert.ThrowsAsync<ServiceError>(() => bare.Service.RenderReport(Vorlage.Id, true, ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }

    async Task<CalcResp> WithGewerbe(string gewerbe, int year)
    {
        var kase = await svc.GetCase(Vorlage.Id, ct);
        kase.Id = "";
        kase.Taxpayer.Gewerbe = gewerbe;
        kase.PeriodFrom = new DateOnly(year, 1, 1);
        kase.PeriodTo = new DateOnly(year, 12, 31);
        return await svc.Calculate((await svc.PutCase(kase, ct)).Id, ct);
    }

    [Fact]
    public async Task AGewerbeIsMeasuredAgainstTheSammlungOfItsYear()
    {
        var calc = await WithGewerbe("56101.0", 2024);

        Assert.Equal(Vergleich.Aufschlag(host.Store.Sammlung(2024), "56101.0", calc.Report.Totals.CalculatedRevenueNet), calc.Rahmen);
        Assert.Equal((2024, 178, 400), (calc.Rahmen!.Jahr, calc.Rahmen.Von, calc.Rahmen.Bis));
        Assert.Equal(Rahmenlage.Über, calc.Rahmen.Lage(calc.Report.Totals.Markup));
        var warning = Assert.Single(calc.Report.Warnings, w => w.Code == "markup-out-of-range");
        Assert.Equal("Rohgewinnaufschlag 405,76 % liegt über dem Rahmensatz 178 bis 400 v.H. der Richtsatzsammlung 2024 für „Gast-, Speise- und Schankwirtschaften“", warning.Message);
    }

    [Fact]
    public async Task AKennzahlOfSeveralKlassenHasNoRahmen()
    {
        var calc = await WithGewerbe("561", 2024);
        Assert.Null(calc.Rahmen);
        Assert.DoesNotContain(calc.Report.Warnings, w => w.Code == "markup-out-of-range");
    }

    [Fact]
    public async Task AYearWithoutItsOwnSammlungTakesTheLatestBefore()
    {
        Assert.Equal(2015, (await WithGewerbe("56101.0", 2017)).Rahmen?.Jahr);
    }

    [Fact]
    public async Task AYearBeforeEverySammlungHasNoRahmen()
    {
        var calc = await WithGewerbe("56101.0", 2005);
        Assert.Null(calc.Rahmen);
        Assert.DoesNotContain(calc.Report.Warnings, w => w.Code == "markup-out-of-range");
    }

    [Fact]
    public async Task ACaseTheCalculationCannotTakeIsInvalid()
    {
        var kase = await svc.GetCase(Vorlage.Id, ct);
        kase.Id = "";
        kase.Pinned.Add(new PinnedPortions { ProductId = "prod.pils.03", Portions = -1, Reason = "Tippfehler" });
        var stored = await svc.PutCase(kase, ct);

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Calculate(stored.Id, ct));

        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.StartsWith("Kalkulation: ", e.Message);
    }
}

using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class CaseTests : IDisposable
{
    readonly Host host = new();
    readonly Services svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public CaseTests() => svc = host.Service;

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task StatusReadsTheSeededRuleSet()
    {
        using var fresh = new TempDir();
        Services first = Services.Local(new RuleStore(fresh.Sub("store"), TestData.Seed()), new CaseStore(fresh.Sub("cases")), "test");
        var status = await first.Rules.Status(ct);
        Assert.Equal(0, status.RulesVersion);
        Assert.Null(status.Problem);
        Assert.Equal("test", status.AppVersion);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), status.RulesDate);
    }

    [Fact]
    public async Task TheRulesDateFollowsTheLatestSave()
    {
        var before = (await svc.Rules.Status(ct)).RulesDate;
        var category = (await svc.Rules.Load(ct)).Categories["cat.spirituosen"];
        category.Meta.ChangedAt = before.AddDays(1);
        await svc.Rules.Save(category, ct);
        Assert.Equal(before.AddDays(1), (await svc.Rules.Status(ct)).RulesDate);
    }

    [Fact]
    public async Task ACaseIsStoredUnderItsOwnId()
    {
        var kase = await host.PutVorlage();
        Assert.Equal(Vorlage.Id, kase.Id);
        Assert.Single(kase.Invoices);
        Assert.Equal(3, (await svc.Cases.Get(Vorlage.Id, ct)).Invoices[0].Lines.Count);
    }

    [Fact]
    public async Task ACaseWithoutAnIdGetsOne()
    {
        var neu = await svc.Cases.Put(Vorlage.Blank(), ct);
        Assert.Equal(7, Guid.Parse(neu.Id).Version);
        Assert.NotEqual(default, neu.CreatedAt);
        Assert.Equal(neu.CreatedAt, neu.UpdatedAt);
        Assert.Contains((await svc.Cases.List(ct)).Cases, c => c.Id == neu.Id);
    }

    [Fact]
    public async Task PuttingACaseAgainKeepsItsCreationTime()
    {
        var neu = await svc.Cases.Put(Vorlage.Blank(), ct);
        var created = neu.CreatedAt;
        neu.Label = "Umbenannt";
        var again = await svc.Cases.Put(neu, ct);
        Assert.Equal(created, again.CreatedAt);
        Assert.Equal("Umbenannt", (await svc.Cases.Get(neu.Id, ct)).Label);
    }

    [Fact]
    public async Task CasesAreListedAndDeleted()
    {
        var a = await svc.Cases.Put(Vorlage.Blank("A"), ct);
        var b = await svc.Cases.Put(Vorlage.Blank("B"), ct);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), (await svc.Cases.List(ct)).Cases.Select(c => c.Id).Order());

        await svc.Cases.Delete(a.Id, ct);

        Assert.Equal([b.Id], (await svc.Cases.List(ct)).Cases.Select(c => c.Id));
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Get(a.Id, ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task AnInvalidCaseIsRefused()
    {
        var kase = Vorlage.Blank(label: " ");
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Put(kase, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.IsType<CaseInvalidException>(e.InnerException);
    }

    [Fact]
    public async Task ARecipeOfTheCaseMayOnlyUseKnownProducts()
    {
        var kase = Vorlage.Blank();
        kase.Products = [new() { ProductId = "prod.pils.05", Vat = 1900, Recipe = [new() { PartId = "prod.unbekannt", Amount = 500, Unit = "MLT" }] }];
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Put(kase, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);

        kase.Products[0].Recipe = [new() { PartId = "prod.bier.fass", Amount = 500, Unit = "MLT" }];
        var saved = await svc.Cases.Put(kase, ct);
        Assert.Equal("prod.bier.fass", Assert.Single((await svc.Cases.Get(saved.Id, ct)).Products[0].Recipe!).PartId);
    }

    [Fact]
    public async Task ARecipeOfTheCaseCountsEachPartInItsOwnUnit()
    {
        var kase = Vorlage.Blank();
        kase.Products = [new() { ProductId = "prod.pils.05", Vat = 1900, Recipe = [new() { PartId = "prod.bier.fass", Amount = 1, Unit = "H87" }] }];

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Put(kase, ct));

        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.Contains("nicht in Stück", e.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../ausbruch")]
    public async Task AMissingOrMalformedCaseIdIsInvalid(string id)
    {
        Assert.Equal(ErrorCode.Invalid, (await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Get(id, ct))).Code);
        Assert.Equal(ErrorCode.Invalid, (await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Delete(id, ct))).Code);
        Assert.Equal(ErrorCode.Invalid, (await Assert.ThrowsAsync<ServiceError>(() => svc.Reports.Calculate(id, ct))).Code);
    }

    public static TheoryData<string, Func<Services, CancellationToken, Task>> UnknownCase => new()
    {
        { "GetCase", (s, ct) => s.Cases.Get("fall-x", ct) },
        { "DeleteCase", (s, ct) => s.Cases.Delete("fall-x", ct) },
        { "ExportCase", (s, ct) => s.Cases.Export("fall-x", ct) },
        { "Calculate", (s, ct) => s.Reports.Calculate("fall-x", ct) },
        { "RenderReport", (s, ct) => s.Reports.Render("fall-x", false, ct) },
        { "SuggestProducts", (s, ct) => s.Assortment.Suggest("fall-x", [], ct) },
        { "MapCase", (s, ct) => s.Mapping.Map("fall-x", ct) },
        { "UnifySuppliers", (s, ct) => s.Invoices.UnifySuppliers("fall-x", [], ct) },
        { "ExportInvoice", (s, ct) => s.Invoices.Export("fall-x", "re-x", ct) },
        { "DeleteInvoice", (s, ct) => s.Invoices.Delete("fall-x", "re-x", ct) },
        { "InvoiceSource", (s, ct) => s.Invoices.Source("fall-x", "re-x", ct) },
    };

    [Theory]
    [MemberData(nameof(UnknownCase))]
    public async Task AnUnknownCaseIsNotFound(string call, Func<Services, CancellationToken, Task> run)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => run(svc, ct));
        Assert.True(e.Code == ErrorCode.NotFound, call + ": " + e.Code);
    }

    public static TheoryData<string, Func<Services, CancellationToken, Task>> UnknownInvoice => new()
    {
        { "ExportInvoice", (s, ct) => s.Invoices.Export(Vorlage.Id, "re-x", ct) },
        { "DeleteInvoice", (s, ct) => s.Invoices.Delete(Vorlage.Id, "re-x", ct) },
        { "InvoiceSource", (s, ct) => s.Invoices.Source(Vorlage.Id, "re-x", ct) },
        { "InvoiceSource of a line-only invoice", (s, ct) => s.Invoices.Source(Vorlage.Id, Vorlage.InvoiceId, ct) },
    };

    [Theory]
    [MemberData(nameof(UnknownInvoice))]
    public async Task AnUnknownInvoiceIsNotFound(string call, Func<Services, CancellationToken, Task> run)
    {
        await host.PutVorlage();
        var e = await Assert.ThrowsAsync<ServiceError>(() => run(svc, ct));
        Assert.True(e.Code == ErrorCode.NotFound, call + ": " + e.Code);
    }

    [Fact]
    public async Task ACaseExportsAndImportsAsOneFileWithItsDocuments()
    {
        var kase = await host.PutVorlage();
        var pdf = File.ReadAllBytes(TestData.File("zugferd.pdf"));
        var stored = await svc.Invoices.Verify(new VerifyReq(kase.Id, new Invoice { Number = "Z" }, Intent.Store, "zugferd.pdf", pdf), ct);

        var dump = await svc.Cases.Export(kase.Id, ct);
        Assert.EndsWith(".db", dump.FileName);
        Assert.StartsWith("Umsatzschätzung-Schankwirtschaft_Zum_Alten_Fass_Bp_2024-", dump.FileName);

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Import(dump.FileName, dump.Data, false, ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.Equal([kase.Label], Assert.IsType<List<string>>(e.Details));

        await svc.Cases.Delete(kase.Id, ct);
        var back = await svc.Cases.Import(dump.FileName, dump.Data, false, ct);
        Assert.Equal(kase.Id, back.Id);
        Assert.Equal(2, back.Invoices.Count);
        var (name, data) = host.Cases.LoadFile(kase.Id, stored.Invoice.Id);
        Assert.Equal("zugferd.pdf", name);
        Assert.Equal(pdf, data);
    }

    [Fact]
    public async Task AnImportMayReplaceTheCaseWhenAsked()
    {
        var kase = await host.PutVorlage();
        var label = kase.Label;
        var dump = await svc.Cases.Export(kase.Id, ct);
        kase.Label = "Geändert";
        await svc.Cases.Put(kase, ct);

        var back = await svc.Cases.Import(dump.FileName, dump.Data, true, ct);

        Assert.Equal(label, back.Label);
        Assert.Equal(label, (await svc.Cases.Get(kase.Id, ct)).Label);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[0])]
    public async Task AFileThatIsNoCaseIsInvalid(byte[] data)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Import("kaputt.db", data, false, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.Empty((await svc.Cases.List(ct)).Cases);
    }

    [Fact]
    public async Task ACaseFileFromANewerVersionIsInvalid()
    {
        var kase = await host.PutVorlage();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(host.Sub("cases"), CaseStore.FileName(kase.Label))};Pooling=false"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 999";
            cmd.ExecuteNonQuery();
        }

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Get(kase.Id, ct));

        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.IsType<SchemaTooNewException>(e.InnerException?.InnerException?.InnerException);
    }

    [Fact]
    public async Task AnUnreachableCaseStoreIsUnavailable()
    {
        var blocked = host.Sub("blockiert");
        File.WriteAllText(blocked, "keine Ablage");
        Services broken = Services.Local(host.Store, new CaseStore(blocked), "test");

        var e = await Assert.ThrowsAsync<ServiceError>(() => broken.Cases.Put(Vorlage.Blank(), ct));

        Assert.Equal(ErrorCode.Unavailable, e.Code);
        Assert.IsType<StoreUnavailableException>(e.InnerException);
    }

    [Fact]
    public async Task AnInvoiceExportsAsCsvWithItsMapping()
    {
        var kase = await host.PutVorlage();

        var export = await svc.Invoices.Export(kase.Id, Vorlage.InvoiceId, ct);

        var csv = System.Text.Encoding.UTF8.GetString(export.Data);
        Assert.Contains("Rechnungsnummer;2024-04711", csv);
        Assert.Contains("Netto;1.690,00 €", csv);
        Assert.Contains("1;Pils Fass 50 l;31090;;12 Keg;92,50 €;1.110,00 €;19 %;Fassbier Pils × 50 l", csv);
        Assert.Matches(@"^Umsatzschätzung-Schankwirtschaft_Zum_Alten_Fass_Bp_2024_2024-04711-\d{4}-\d{2}-\d{2}\.csv$", export.FileName);
    }

    [Fact]
    public async Task AReadAssortmentJoinsTheCaseAndExportsAgain()
    {
        var kase = await host.PutVorlage();
        var product = TestData.Seed().Products.Values.First();

        var read = await svc.Assortment.Read(System.Text.Encoding.UTF8.GetBytes($"Produkt-ID;Bruttopreis;USt\n{product.Id};3,90;19\nfehlt;1;7\n"), ct);

        Assert.Equal(["fehlt"], read.Unknown);
        var listed = Assert.Single(read.Products);
        Assert.Equal((product.Id, 390L, 1900L), (listed.ProductId, listed.GrossPrice, listed.Vat));
        kase.Products.RemoveAll(p => p.ProductId == product.Id);
        kase.Products.Add(listed);
        await svc.Cases.Put(kase, ct);
        var export = await svc.Assortment.Export(kase.Id, ct);
        Assert.Contains(";3,90 €;19 %;" + product.Id, System.Text.Encoding.UTF8.GetString(export.Data));
        Assert.Matches(@"^Umsatzschätzung-Schankwirtschaft_Zum_Alten_Fass_Bp_2024_Sortiment-\d{4}-\d{2}-\d{2}\.csv$", export.FileName);
    }

    [Theory]
    [InlineData("Schankwirtschaft Zum Alten Fass, Bp 2024", "Schankwirtschaft_Zum_Alten_Fass_Bp_2024")]
    [InlineData("Größe Übung", "Grose_Ubung")]
    [InlineData("?!", "Prüfung")]
    public async Task AnExportIsNamedAfterTheCaseLabel(string label, string name)
    {
        var kase = await svc.Cases.Put(Vorlage.Blank(label), ct);
        var dump = await svc.Cases.Export(kase.Id, ct);
        Assert.Equal($"Umsatzschätzung-{name}-{DateTime.Now:yyyy-MM-dd}.db", dump.FileName);
    }

    public static TheoryData<string, Func<Services, CancellationToken, Task>> AnyCall => new()
    {
        { "Status", (s, ct) => s.Rules.Status(ct) },
        { "Rules", (s, ct) => s.Rules.Load(ct) },
        { "ListCases", (s, ct) => s.Cases.List(ct) },
        { "PutCase", (s, ct) => s.Cases.Put(Vorlage.Blank(), ct) },
        { "DeleteCase", (s, ct) => s.Cases.Delete(Vorlage.Id, ct) },
        { "SaveRule", (s, ct) => s.Rules.Save(new Category { Id = "cat.neu", Name = "Neu" }, ct) },
        { "Calculate", (s, ct) => s.Reports.Calculate(Vorlage.Id, ct) },
        { "SuggestProducts", (s, ct) => s.Assortment.Suggest(Vorlage.Id, [], ct) },
        { "SuggestMapping", (s, ct) => s.Mapping.Suggest("", new InvoiceLine { Name = "Pils" }, null, ct) },
        { "MapCase", (s, ct) => s.Mapping.Map(Vorlage.Id, ct) },
        { "UnifySuppliers", (s, ct) => s.Invoices.UnifySuppliers(Vorlage.Id, [], ct) },
        { "VerifyInvoice", (s, ct) => s.Invoices.Verify(new VerifyReq(Vorlage.Id, new Invoice(), Intent.Store, null, null), ct) },
    };

    [Theory]
    [MemberData(nameof(AnyCall))]
    public async Task ACancelledCallDoesNothing(string call, Func<Services, CancellationToken, Task> run)
    {
        await host.PutVorlage();
        var before = (await svc.Rules.Load(ct)).Version;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run(svc, new CancellationToken(true)));

        Assert.True(before == (await svc.Rules.Load(ct)).Version, call);
        Assert.Equal([Vorlage.Id], (await svc.Cases.List(ct)).Cases.Select(c => c.Id));
        Assert.Single((await svc.Cases.Get(Vorlage.Id, ct)).Invoices);
    }
}

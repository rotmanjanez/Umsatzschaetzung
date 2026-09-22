using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class CaseTests : IDisposable
{
    readonly Host host = new();
    readonly IService svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public CaseTests() => svc = host.Service;

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task StatusReadsTheSeededRuleSet()
    {
        using var fresh = new TempDir();
        IService first = new LocalService(new RuleStore(fresh.Sub("store"), TestData.Seed()), new CaseStore(fresh.Sub("cases")), null, new(), null, null, "test");
        var status = await first.Status(ct);
        Assert.Equal(0, status.RulesVersion);
        Assert.Null(status.Problem);
        Assert.Equal("test", status.AppVersion);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 0, 0, 0, TimeSpan.Zero), status.RulesDate);
    }

    [Fact]
    public async Task TheRulesDateFollowsTheLatestSave()
    {
        var before = (await svc.Status(ct)).RulesDate;
        var category = (await svc.Rules(ct)).Categories["cat.spirituosen"];
        category.Meta.ChangedAt = before.AddDays(1);
        await svc.SaveRule(category, ct);
        Assert.Equal(before.AddDays(1), (await svc.Status(ct)).RulesDate);
    }

    [Fact]
    public async Task ACaseIsStoredUnderItsOwnId()
    {
        var kase = await host.PutVorlage();
        Assert.Equal(Vorlage.Id, kase.Id);
        Assert.Single(kase.Invoices);
        Assert.Equal(3, (await svc.GetCase(Vorlage.Id, ct)).Invoices[0].Lines.Count);
    }

    [Fact]
    public async Task ACaseWithoutAnIdGetsOne()
    {
        var neu = await svc.PutCase(Vorlage.Blank(), ct);
        Assert.StartsWith("fall-", neu.Id);
        Assert.NotEqual(default, neu.CreatedAt);
        Assert.Equal(neu.CreatedAt, neu.UpdatedAt);
        Assert.Contains(await svc.ListCases(ct), c => c.Id == neu.Id);
    }

    [Fact]
    public async Task PuttingACaseAgainKeepsItsCreationTime()
    {
        var neu = await svc.PutCase(Vorlage.Blank(), ct);
        var created = neu.CreatedAt;
        neu.Label = "Umbenannt";
        var again = await svc.PutCase(neu, ct);
        Assert.Equal(created, again.CreatedAt);
        Assert.Equal("Umbenannt", (await svc.GetCase(neu.Id, ct)).Label);
    }

    [Fact]
    public async Task CasesAreListedAndDeleted()
    {
        var a = await svc.PutCase(Vorlage.Blank("A"), ct);
        var b = await svc.PutCase(Vorlage.Blank("B"), ct);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), (await svc.ListCases(ct)).Select(c => c.Id).Order());

        await svc.DeleteCase(a.Id, ct);

        Assert.Equal([b.Id], (await svc.ListCases(ct)).Select(c => c.Id));
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.GetCase(a.Id, ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task AnInvalidCaseIsRefused()
    {
        var kase = Vorlage.Blank(label: " ");
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.PutCase(kase, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.IsType<CaseInvalidException>(e.InnerException);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../ausbruch")]
    public async Task AMissingOrMalformedCaseIdIsInvalid(string id)
    {
        Assert.Equal(ErrorCode.Invalid, (await Assert.ThrowsAsync<ServiceError>(() => svc.GetCase(id, ct))).Code);
        Assert.Equal(ErrorCode.Invalid, (await Assert.ThrowsAsync<ServiceError>(() => svc.DeleteCase(id, ct))).Code);
        Assert.Equal(ErrorCode.Invalid, (await Assert.ThrowsAsync<ServiceError>(() => svc.Calculate(id, ct))).Code);
    }

    public static TheoryData<string, Func<IService, CancellationToken, Task>> UnknownCase => new()
    {
        { "GetCase", (s, ct) => s.GetCase("fall-x", ct) },
        { "DeleteCase", (s, ct) => s.DeleteCase("fall-x", ct) },
        { "ExportCase", (s, ct) => s.ExportCase("fall-x", ct) },
        { "Calculate", (s, ct) => s.Calculate("fall-x", ct) },
        { "RenderReport", (s, ct) => s.RenderReport("fall-x", false, ct) },
        { "MapCase", (s, ct) => s.MapCase("fall-x", ct) },
        { "ExportInvoice", (s, ct) => s.ExportInvoice("fall-x", "re-x", ct) },
        { "DeleteInvoice", (s, ct) => s.DeleteInvoice("fall-x", "re-x", ct) },
        { "InvoiceSource", (s, ct) => s.InvoiceSource("fall-x", "re-x", ct) },
    };

    [Theory]
    [MemberData(nameof(UnknownCase))]
    public async Task AnUnknownCaseIsNotFound(string call, Func<IService, CancellationToken, Task> run)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => run(svc, ct));
        Assert.True(e.Code == ErrorCode.NotFound, call + ": " + e.Code);
    }

    public static TheoryData<string, Func<IService, CancellationToken, Task>> UnknownInvoice => new()
    {
        { "ExportInvoice", (s, ct) => s.ExportInvoice(Vorlage.Id, "re-x", ct) },
        { "DeleteInvoice", (s, ct) => s.DeleteInvoice(Vorlage.Id, "re-x", ct) },
        { "InvoiceSource", (s, ct) => s.InvoiceSource(Vorlage.Id, "re-x", ct) },
        { "InvoiceSource of a line-only invoice", (s, ct) => s.InvoiceSource(Vorlage.Id, Vorlage.InvoiceId, ct) },
    };

    [Theory]
    [MemberData(nameof(UnknownInvoice))]
    public async Task AnUnknownInvoiceIsNotFound(string call, Func<IService, CancellationToken, Task> run)
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
        var stored = await svc.VerifyInvoice(new VerifyReq(kase.Id, new Invoice { Number = "Z" }, Intent.Store, "zugferd.pdf", pdf), ct);

        var dump = await svc.ExportCase(kase.Id, ct);
        Assert.EndsWith(".db", dump.FileName);
        Assert.StartsWith("Umsatzschätzung-Schankwirtschaft_Zum_Alten_Fass_Bp_2024-", dump.FileName);

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.ImportCase(dump.FileName, dump.Data, false, ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.Equal(kase.Label, e.Details);

        await svc.DeleteCase(kase.Id, ct);
        var back = await svc.ImportCase(dump.FileName, dump.Data, false, ct);
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
        var dump = await svc.ExportCase(kase.Id, ct);
        kase.Label = "Geändert";
        await svc.PutCase(kase, ct);

        var back = await svc.ImportCase(dump.FileName, dump.Data, true, ct);

        Assert.Equal(label, back.Label);
        Assert.Equal(label, (await svc.GetCase(kase.Id, ct)).Label);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[0])]
    public async Task AFileThatIsNoCaseIsInvalid(byte[] data)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.ImportCase("kaputt.db", data, false, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.Empty(await svc.ListCases(ct));
    }

    [Fact]
    public async Task ACaseFileFromANewerVersionIsInvalid()
    {
        var kase = await host.PutVorlage();
        using (var db = new SqliteConnection($"Data Source={Path.Combine(host.Sub("cases"), kase.Id + ".db")};Pooling=false"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "PRAGMA user_version = 999";
            cmd.ExecuteNonQuery();
        }

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.GetCase(kase.Id, ct));

        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.IsType<SchemaTooNewException>(e.InnerException?.InnerException?.InnerException);
    }

    [Fact]
    public async Task AnUnreachableCaseStoreIsUnavailable()
    {
        var blocked = host.Sub("blockiert");
        File.WriteAllText(blocked, "keine Ablage");
        IService broken = new LocalService(host.Store, new CaseStore(blocked), null, new(), null, null, "test");

        var e = await Assert.ThrowsAsync<ServiceError>(() => broken.PutCase(Vorlage.Blank(), ct));

        Assert.Equal(ErrorCode.Unavailable, e.Code);
        Assert.IsType<StoreUnavailableException>(e.InnerException);
    }

    [Fact]
    public async Task AnInvoiceExportsAsCsvWithItsMapping()
    {
        var kase = await host.PutVorlage();

        var export = await svc.ExportInvoice(kase.Id, Vorlage.InvoiceId, ct);

        var csv = System.Text.Encoding.UTF8.GetString(export.Data);
        Assert.Contains("Rechnungsnummer;2024-04711", csv);
        Assert.Contains("Netto;1.690,00 €", csv);
        Assert.Contains("1;Pils Fass 50 l;31090;;12 Keg;92,50 €;1.110,00 €;19 %;Fassbier Pils × 50 l", csv);
        Assert.Matches(@"^Umsatzschätzung-Schankwirtschaft_Zum_Alten_Fass_Bp_2024_2024-04711-\d{4}-\d{2}-\d{2}\.csv$", export.FileName);
    }

    [Theory]
    [InlineData("Schankwirtschaft Zum Alten Fass, Bp 2024", "Schankwirtschaft_Zum_Alten_Fass_Bp_2024")]
    [InlineData("Größe Übung", "Grose_Ubung")]
    [InlineData("?!", "Prüfung")]
    public async Task AnExportIsNamedAfterTheCaseLabel(string label, string name)
    {
        var kase = await svc.PutCase(Vorlage.Blank(label), ct);
        var dump = await svc.ExportCase(kase.Id, ct);
        Assert.Equal($"Umsatzschätzung-{name}-{DateTime.Now:yyyy-MM-dd}.db", dump.FileName);
    }

    public static TheoryData<string, Func<IService, CancellationToken, Task>> AnyCall => new()
    {
        { "Status", (s, ct) => s.Status(ct) },
        { "Rules", (s, ct) => s.Rules(ct) },
        { "ListCases", (s, ct) => s.ListCases(ct) },
        { "PutCase", (s, ct) => s.PutCase(Vorlage.Blank(), ct) },
        { "DeleteCase", (s, ct) => s.DeleteCase(Vorlage.Id, ct) },
        { "SaveRule", (s, ct) => s.SaveRule(new Category { Id = "cat.neu", Name = "Neu" }, ct) },
        { "Calculate", (s, ct) => s.Calculate(Vorlage.Id, ct) },
        { "SuggestMapping", (s, ct) => s.SuggestMapping("", new InvoiceLine { Name = "Pils" }, null, ct) },
        { "MapCase", (s, ct) => s.MapCase(Vorlage.Id, ct) },
        { "VerifyInvoice", (s, ct) => s.VerifyInvoice(new VerifyReq(Vorlage.Id, new Invoice(), Intent.Store, null, null), ct) },
    };

    [Theory]
    [MemberData(nameof(AnyCall))]
    public async Task ACancelledCallDoesNothing(string call, Func<IService, CancellationToken, Task> run)
    {
        await host.PutVorlage();
        var before = (await svc.Rules(ct)).Version;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run(svc, new CancellationToken(true)));

        Assert.True(before == (await svc.Rules(ct)).Version, call);
        Assert.Equal([Vorlage.Id], (await svc.ListCases(ct)).Select(c => c.Id));
        Assert.Single((await svc.GetCase(Vorlage.Id, ct)).Invoices);
    }
}

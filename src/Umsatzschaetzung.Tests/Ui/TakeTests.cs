using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

// A write of the whole case that overlaps the store of an invoice writes the case without it; the
// window writes the case again once it takes the invoice in, so the invoice is not lost.
public class TakeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnInvoiceStoredWhileTheCaseIsWrittenIsKept(bool writeStartsAfter)
    {
        var ct = TestContext.Current.CancellationToken;
        using var host = new Host();
        var kase = await host.PutVorlage("case.take");
        var held = new Held(host.Service.Cases);
        var service = host.Service with { Cases = held };
        var session = new Session(service);
        session.Open(await service.Cases.Get(kase.Id, ct));

        var before = session.Puts;
        session.Case!.Taxpayer.Name = "Gasthaus Neu";
        var writing = session.SaveCase(new Place(session.History, 0), ct);
        var since = writeStartsAfter ? before : session.Puts;
        var stored = await service.Invoices.Verify(new VerifyReq(kase.Id, Paper(), Intent.Store, null, null), ct);
        held.Go.SetResult();
        await writing;
        session.Take(kase.Id, stored.Invoice, stored.Stored!, since);
        await session.Saved;

        Assert.Contains((await service.Cases.Get(kase.Id, ct)).Invoices, i => i.Id == stored.Invoice.Id);
    }

    static Invoice Paper() => new()
    {
        Source = Source.Ubl, SupplierName = "Papier Müller", Number = "PM-1", Date = new DateOnly(2024, 2, 1),
        StatedNet = 500, StatedGross = 595,
        Lines = [new InvoiceLine { No = 1, Name = "Servietten 3-lagig 250 Stk", Quantity = 1000, UnitPrice = 5_000_000, PriceBaseQty = 1000, LineNet = 500, Vat = 1900, UnitCode = "KGM" }],
    };

    // A write of the case waits until it is let go, with the case as it was handed over.
    sealed class Held(ICases inner) : ICases
    {
        public TaskCompletionSource Go { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<Case> Put(Case kase, CancellationToken ct)
        {
            await Go.Task;
            return await inner.Put(kase, ct);
        }

        public Task<CasesResp> List(CancellationToken ct) => inner.List(ct);
        public Task<Case> Get(string caseId, CancellationToken ct) => inner.Get(caseId, ct);
        public Task Delete(string caseId, CancellationToken ct) => inner.Delete(caseId, ct);
        public Task<Case> Import(string fileName, byte[] data, bool overwrite, CancellationToken ct) => inner.Import(fileName, data, overwrite, ct);
        public Task<ExportResp> Export(string caseId, CancellationToken ct) => inner.Export(caseId, ct);
    }
}

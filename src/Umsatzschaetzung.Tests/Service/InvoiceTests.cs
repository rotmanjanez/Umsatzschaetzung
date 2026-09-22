using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

[Collection(MatcherCollection.Name)]
public class InvoiceTests(MatcherHost host)
{
    static readonly byte[] Zugferd = File.ReadAllBytes(TestData.File("zugferd.pdf"));
    static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];

    readonly IService svc = host.Service;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    Task<Case> NewCase() => svc.PutCase(Vorlage.Blank(), ct);

    // 1 kg at 5,00 € the kilo, 5,00 € net and 5,95 € gross, as the supplier prints it.
    static Invoice Clean(string unitCode = "KGM", long lineNet = 500, long? statedGross = 595) => new()
    {
        Source = Source.Ubl, SupplierName = "Papier Müller", Number = "PM-1", Date = new DateOnly(2024, 2, 1),
        StatedNet = 500, StatedGross = statedGross,
        Lines = [new InvoiceLine { No = 1, Name = "Servietten 3-lagig 250 Stk", Quantity = 1000, UnitPrice = 5_000_000, PriceBaseQty = 1000, LineNet = lineNet, Vat = 1900, UnitCode = unitCode }],
    };

    [Fact]
    public async Task AZugferdPdfIsParsedAttachedAndLeftUnmapped()
    {
        var kase = await NewCase();

        var parsed = await svc.ParseInvoice(kase.Id, "zugferd.pdf", Zugferd, ct);

        Assert.False(parsed.NeedsOcr);
        Assert.Equal("RE-20201121/508", parsed.Invoice.Number);
        Assert.Equal(3, parsed.Invoice.Lines.Count);
        Assert.Equal([1, 2, 3], parsed.UnmappedLines);
        Assert.StartsWith("re-", parsed.Invoice.Id);
        Assert.Equal([parsed.Invoice.Id], parsed.Case!.Invoices.Select(i => i.Id));
        Assert.Equal("zugferd.pdf", host.Cases.LoadFile(kase.Id, parsed.Invoice.Id).Name);
    }

    [Fact]
    public async Task AnInvoiceParsedWithoutACaseIsNotAttached()
    {
        var parsed = await svc.ParseInvoice("", "zugferd.pdf", Zugferd, ct);
        Assert.Equal(3, parsed.Invoice.Lines.Count);
        Assert.Null(parsed.Case);
    }

    [Fact]
    public async Task AnXmlInvoiceIsParsed()
    {
        var xml = File.ReadAllBytes(TestData.Fixture("dataset/2025/spirituosen/2025-10-01_RE2503135.xml"));
        var parsed = await svc.ParseInvoice("", "re.xml", xml, ct);
        Assert.False(parsed.NeedsOcr);
        Assert.Equal(Source.Ubl, parsed.Invoice.Source);
        Assert.NotEmpty(parsed.Invoice.Lines);
    }

    [Fact]
    public async Task AnInvoiceForAnUnknownCaseIsNotFound()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.ParseInvoice("fall-gibt-es-nicht", "zugferd.pdf", Zugferd, ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task AScanNeedsOcrAndIsNotStored()
    {
        var kase = await NewCase();
        var parsed = await svc.ParseInvoice(kase.Id, "scan.png", Png, ct);
        Assert.True(parsed.NeedsOcr);
        Assert.Null(parsed.Case);
        Assert.Empty((await svc.GetCase(kase.Id, ct)).Invoices);
    }

    [Fact]
    public async Task AnEmptyFileIsInvalid()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.ParseInvoice("", "leer.pdf", [], ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
    }

    [Fact]
    public async Task AnUnknownFormatIsUnsupported()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.ParseInvoice("", "notiz.txt", "Hallo"u8.ToArray(), ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }

    [Fact]
    public async Task APdfPreviewWithoutARendererIsUnsupported()
    {
        var kase = await NewCase();
        var parsed = await svc.ParseInvoice(kase.Id, "zugferd.pdf", Zugferd, ct);
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.InvoiceSource(kase.Id, parsed.Invoice.Id, ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }

    [Fact]
    public async Task DeletingAnInvoiceRemovesItsStoredFile()
    {
        var kase = await NewCase();
        var parsed = await svc.ParseInvoice(kase.Id, "zugferd.pdf", Zugferd, ct);

        var after = await svc.DeleteInvoice(kase.Id, parsed.Invoice.Id, ct);

        Assert.Empty(after.Invoices);
        Assert.Empty((await svc.GetCase(kase.Id, ct)).Invoices);
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.InvoiceSource(kase.Id, parsed.Invoice.Id, ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task OcrWithoutAnEngineIsUnsupported()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.OcrInvoice("", "scan.png", Png, ct));
        Assert.Equal(ErrorCode.Unsupported, e.Code);
    }

    [Fact]
    public async Task OcrOfAnEmptyFileIsInvalid()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.OcrInvoice("", "scan.png", [], ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
    }

    [Fact]
    public async Task CheckLooksWithoutStoring()
    {
        var kase = await NewCase();
        var inv = Clean(unitCode: "");

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, inv, Intent.Check, "r.png", Png), ct);

        Assert.Contains(v.Flags, f => f.Code == "no_unit");
        Assert.False(v.Blocked);
        Assert.False(v.Accepted);
        Assert.Null(v.Case);
        Assert.Null(v.Invoice.Verification);
        Assert.StartsWith("re-", v.Invoice.Id);
        Assert.Empty((await svc.GetCase(kase.Id, ct)).Invoices);
    }

    [Fact]
    public async Task CheckNeedsNoCase()
    {
        var v = await svc.VerifyInvoice(new VerifyReq("", Clean(), Intent.Check, null, null), ct);
        Assert.Empty(v.Flags);
        Assert.Null(v.Case);
    }

    [Fact]
    public async Task StoreKeepsTheInvoiceForReview()
    {
        var kase = await NewCase();
        var reading = new List<OcrPage> { new() { Width = 10, Height = 20, Words = [new OcrWord { Text = "Servietten" }] } };

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(), Intent.Store, "r.png", Png, reading), ct);

        Assert.False(v.Accepted);
        Assert.Null(v.Invoice.Verification);
        var stored = Assert.Single((await svc.GetCase(kase.Id, ct)).Invoices);
        Assert.Equal(v.Invoice.Id, stored.Id);
        Assert.Null(stored.Verification);
        var source = await svc.InvoiceSource(kase.Id, stored.Id, ct);
        Assert.Equal("r.png", source.FileName);
        Assert.Equal(Png, Assert.Single(source.Pages).Image);
        var read = Assert.Single((await svc.InvoiceReading(kase.Id, stored.Id, ct)).Pages);
        Assert.Equal("Servietten", Assert.Single(read.Words).Text);
        Assert.Equal(Png, read.Image);
    }

    [Theory]
    [InlineData(Intent.Store)]
    [InlineData(Intent.Confirm)]
    [InlineData(Intent.Auto)]
    public async Task AnIntentThatStoresNeedsACase(Intent intent)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.VerifyInvoice(new VerifyReq("", Clean(), intent, null, null), ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
    }

    [Theory]
    [InlineData(Intent.Store)]
    [InlineData(Intent.Confirm)]
    [InlineData(Intent.Auto)]
    public async Task AnIntentThatStoresNeedsAnExistingCase(Intent intent)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.VerifyInvoice(new VerifyReq("fall-gibt-es-nicht", Clean(), intent, null, null), ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }

    [Fact]
    public async Task AnUnknownCaseLearnsNoRule()
    {
        static Invoice Keg()
        {
            var inv = Clean();
            inv.SupplierName = "Brauerei Unbekannt";
            inv.Lines[0].Name = "Pils vom Fass 30 l Keg";
            return inv;
        }
        var before = (await svc.Rules(ct)).Version;

        await Assert.ThrowsAsync<ServiceError>(() => svc.VerifyInvoice(new VerifyReq("fall-gibt-es-nicht", Keg(), Intent.Auto, null, null), ct));
        Assert.Equal(before, (await svc.Rules(ct)).Version);

        var learnt = await svc.VerifyInvoice(new VerifyReq((await NewCase()).Id, Keg(), Intent.Auto, null, null), ct);
        Assert.StartsWith("map-", learnt.Invoice.Lines[0].MappingId);
        Assert.Equal(before + 1, (await svc.Rules(ct)).Version);
    }

    [Fact]
    public async Task ConfirmTakesOverAnInvoiceThatAddsUp()
    {
        var kase = await NewCase();

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(), Intent.Confirm, null, null), ct);

        Assert.True(v.Accepted);
        Assert.False(v.Blocked);
        Assert.Equal(false, v.Invoice.Verification?.Auto);
        Assert.NotNull(Assert.Single(v.Case!.Invoices).Verification);
        Assert.NotNull(Assert.Single((await svc.GetCase(kase.Id, ct)).Invoices).Verification);
    }

    [Fact]
    public async Task ConfirmTakesOverDespiteAFlagThatDoesNotBlock()
    {
        var kase = await NewCase();

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(statedGross: 700), Intent.Confirm, null, null), ct);

        Assert.Contains(v.Flags, f => f.Code == "gross_check");
        Assert.False(v.Blocked);
        Assert.True(v.Accepted);
    }

    [Fact]
    public async Task ALineThatDoesNotAddUpBlocksConfirm()
    {
        var kase = await NewCase();

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(lineNet: 600), Intent.Confirm, null, null), ct);

        Assert.Contains(v.Flags, f => f.Code == "line_total");
        Assert.True(v.Blocked);
        Assert.False(v.Accepted);
        Assert.Null(v.Case);
        Assert.Empty((await svc.GetCase(kase.Id, ct)).Invoices);
    }

    [Fact]
    public async Task ALineSumWithoutAPrintedTotalBlocksConfirm()
    {
        var kase = await NewCase();
        var inv = Clean();
        inv.Source = Source.Scan;
        inv.StatedNet = null;
        inv.NetTotal = 999;

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, inv, Intent.Confirm, null, null), ct);

        Assert.Equal((500, 595), (v.Invoice.NetTotal, v.Invoice.GrossTotal));
        Assert.False(v.Blocked);
        Assert.True(v.Accepted);
    }

    [Fact]
    public async Task AutoTakesOverACompleteReadingThatAddsUp()
    {
        var kase = await NewCase();

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(), Intent.Auto, null, null), ct);

        Assert.True(v.Accepted);
        Assert.Equal(true, v.Invoice.Verification?.Auto);
        Assert.Equal(true, Assert.Single((await svc.GetCase(kase.Id, ct)).Invoices).Verification?.Auto);
    }

    [Fact]
    public async Task AutoKeepsAnIncompleteReadingForReview()
    {
        var kase = await NewCase();
        var inv = Clean();
        inv.Number = "";

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, inv, Intent.Auto, null, null), ct);

        Assert.Empty(v.Flags);
        Assert.False(v.Accepted);
        Assert.Null(Assert.Single((await svc.GetCase(kase.Id, ct)).Invoices).Verification);
    }

    [Fact]
    public async Task AutoKeepsAFlaggedReadingForReview()
    {
        var kase = await NewCase();

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(statedGross: 700), Intent.Auto, null, null), ct);

        Assert.False(v.Blocked);
        Assert.False(v.Accepted);
        Assert.Single((await svc.GetCase(kase.Id, ct)).Invoices);
    }

    [Fact]
    public async Task VerifyingAStoredInvoiceAgainReplacesIt()
    {
        var kase = await NewCase();
        var first = await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(), Intent.Store, null, null), ct);
        var edited = first.Invoice;
        edited.Number = "PM-2";

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, edited, Intent.Confirm, null, null), ct);

        Assert.Equal(first.Invoice.Id, v.Invoice.Id);
        Assert.Equal("PM-2", Assert.Single(v.Case!.Invoices).Number);
    }

    [Fact]
    public async Task AnInvoiceTakenBackForReviewIsNoLongerVerified()
    {
        var kase = await NewCase();
        var confirmed = (await svc.VerifyInvoice(new VerifyReq(kase.Id, Clean(), Intent.Confirm, null, null), ct)).Invoice;
        Assert.NotNull(confirmed.Verification);

        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, confirmed, Intent.Store, null, null), ct);

        Assert.Null(v.Invoice.Verification);
        Assert.Null(Assert.Single((await svc.GetCase(kase.Id, ct)).Invoices).Verification);
    }

    [Fact]
    public async Task AnInvoiceNeverScannedHasNoReading()
    {
        var kase = await NewCase();
        var parsed = await svc.ParseInvoice(kase.Id, "zugferd.pdf", Zugferd, ct);
        Assert.Empty((await svc.InvoiceReading(kase.Id, parsed.Invoice.Id, ct)).Pages);
        Assert.Empty((await svc.InvoiceReading(kase.Id, "re-gibt-es-nicht", ct)).Pages);
    }
}

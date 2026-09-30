using System.Text;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Tagging;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

sealed class LocalInvoices(RuleStore rules, LocalCases cases, LocalMapping mapping, IDocuments? documents, ITagger? tagger, Readings? readings) : IInvoices
{
    IDocuments Documents => documents ?? throw new ServiceError(ErrorCode.Unsupported, "Belege lassen sich hier nicht lesen");

    public Task<ParseResp> Parse(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(ct, async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        switch (InvoiceParser.Detect(data))
        {
            case Kind.Pdf or Kind.Image:
                return new ParseResp(new Invoice(), [], true, null);
            case Kind.Unknown:
                throw new ServiceError(ErrorCode.Unsupported, $"Dateiformat von \"{fileName}\" nicht erkannt");
        }
        var kase = caseId == "" ? null : cases.Load(caseId);
        var made = new Dictionary<string, ArticleMapping>();
        var inv = InvoiceParser.Parse(fileName, data);
        inv.Id = Ids.New();
        var (_, unmapped) = await mapping.Lines(rules.Load().With(kase?.Mappings), inv, kase?.Taxpayer.Gewerbe ?? "", true, made, ct);
        var stored = kase is null ? null : Attach(caseId, inv, fileName, data, made);
        return new ParseResp(inv, unmapped, false, stored);
    });

    public Task<OcrResp> Ocr(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(ct, async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        var (pages, draft) = await Read(fileName, data, ct);
        draft.Id = Ids.New();
        draft.FileName = fileName;
        (draft.NetTotal, draft.GrossTotal) = InvoiceMath.LineTotals(draft.Lines);
        var kase = cases.Find(caseId);
        await mapping.Lines(rules.Load().With(kase?.Mappings), draft, kase?.Taxpayer.Gewerbe ?? "", false, kase?.Mappings ?? [], ct);
        return new OcrResp(draft.Id, pages, draft);
    });

    async Task<(List<OcrPage>, Invoice)> Read(string fileName, byte[] data, CancellationToken ct)
    {
        if (readings?.Find(data) is { } known) return (known.Pages, known.Draft);
        if (documents is null || tagger is null) throw new ServiceError(ErrorCode.Unsupported, "Texterkennung nicht verfügbar");
        var pages = await documents.Read(fileName, data, ct);
        var draft = await Extractor.InvoiceAsync(tagger, pages, ct, (at, regions, ct) => documents.Reread(data, pages[at], at, regions, ct));
        readings?.Keep(data, new OcrResp("", pages, draft));
        return (pages, draft);
    }

    public Task<VerifyResp> Verify(VerifyReq req, CancellationToken ct) => Guard(ct, async () =>
    {
        var inv = req.Invoice;
        if (inv.Id == "") inv.Id = Ids.New();
        if (inv.Source == Model.Source.Scan)
            (inv.NetTotal, inv.GrossTotal) = InvoiceMath.LineTotals(inv.Lines);
        var flags = Check.Invoice(inv);
        var blocked = flags.Any(f => Check.Blocks(inv, f));
        var confirm = req.Intent switch
        {
            Intent.Confirm => !blocked,
            Intent.Auto => Check.Complete(inv, flags),
            _ => false,
        };
        var store = confirm || req.Intent is Intent.Store or Intent.Auto;
        var kase = store ? cases.Load(req.CaseId) : cases.Find(req.CaseId);
        var made = new Dictionary<string, ArticleMapping>();
        await mapping.Lines(rules.Load().With(kase?.Mappings), inv, kase?.Taxpayer.Gewerbe ?? "", confirm, made, ct);
        var resp = new VerifyResp(inv, flags, blocked, false, null);
        if (!store) return resp;
        inv.Verification = confirm ? new Verification { At = Clock.Now(), Auto = req.Intent == Intent.Auto } : null;
        var images = req is { Reading: { } reading, Data: { } data } && documents is not null
            ? await documents.Keep(data, reading, ct).ToListAsync(ct)
            : null;
        var stored = Attach(req.CaseId, inv, req.FileName ?? inv.FileName, req.Data ?? [], made, req.Reading, images);
        return resp with { Stored = stored, Accepted = confirm };
    });

    public Task<Case> Delete(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = cases.Load(caseId);
        var i = c.Invoices.FindIndex(x => x.Id == invoiceId);
        if (i < 0) throw new ServiceError(ErrorCode.NotFound, $"Rechnung \"{invoiceId}\" nicht im Fall \"{caseId}\"");
        c.Invoices.RemoveAt(i);
        cases.Save(c);
        return c;
    });

    public Task<ExportResp> Export(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = cases.Load(caseId);
        var inv = c.Invoices.Find(i => i.Id == invoiceId)
            ?? throw new ServiceError(ErrorCode.NotFound, $"Rechnung \"{invoiceId}\" nicht gefunden");
        var label = inv.Number != "" ? inv.Number : inv.FileName;
        return new ExportResp(Csv.Invoice(c, inv, rules.Load().With(c.Mappings)), FileName(c.Label + " " + label, "csv"));
    });

    public Task<InvoiceSourceResp> Source(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, async () =>
    {
        string name;
        byte[] data;
        try
        {
            (name, data) = cases.Store.LoadFile(caseId, invoiceId);
        }
        catch (CaseNotFoundException e)
        {
            throw new ServiceError(ErrorCode.NotFound, $"kein Beleg zu Rechnung {invoiceId} gespeichert", inner: e);
        }
        if (InvoiceParser.Detect(data) is not (Kind.Image or Kind.Pdf or Kind.Zugferd))
            return new InvoiceSourceResp(name, [new SourcePage(null, Encoding.UTF8.GetString(data))]);
        var read = cases.Store.LoadReading(caseId, invoiceId)?.Select(p => p.Correction).ToList() ?? [];
        var pages = new List<SourcePage>();
        await foreach (var image in Documents.Preview(data, read, ct))
            pages.Add(new SourcePage(image, null));
        return new InvoiceSourceResp(name, pages);
    });

    public Task<InvoiceReadingResp> Reading(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, () =>
    {
        if (cases.Store.LoadReading(caseId, invoiceId) is not { } pages) return new InvoiceReadingResp([]);
        if (documents is not null)
            foreach (var (page, kept) in pages.Zip(cases.Store.LoadImages(caseId, invoiceId)))
                page.Image = documents.Show(kept);
        return new InvoiceReadingResp(pages);
    });

    public Task<Raster?> Snippet(string caseId, string invoiceId, int line, string name, CancellationToken ct) =>
        Guard(ct, () => cases.Excerpts.Row(Documents, caseId, invoiceId, line, name));

    // A line mapped by article number under the old name is asked again under the new one.
    public Task<Case> UnifySuppliers(string caseId, List<string> invoiceIds, CancellationToken ct) => Guard(ct, () =>
    {
        var c = cases.Load(caseId);
        if (!Suppliers.Unify(c, invoiceIds.ToHashSet())) return c;
        c.MappedStore = null;
        cases.Save(c);
        return c;
    });

    Stored Attach(string caseId, Invoice inv, string fileName, byte[] data, Dictionary<string, ArticleMapping> made, List<OcrPage>? reading = null, List<byte[]>? images = null) =>
        new(made, cases.SaveInvoice(caseId, inv, made, new Attachment(inv.Id, fileName, data, reading, images)));
}

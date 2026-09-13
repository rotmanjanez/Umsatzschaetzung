using System.Security.Cryptography;
using System.Text;
using Ausbeute.Calc;
using Ausbeute.Casefile;
using Ausbeute.Extract;
using Ausbeute.Invoices;
using Ausbeute.Model;
using Ausbeute.Reports;
using Ausbeute.Rules;
using Ausbeute.Rulestore;
using Ausbeute.Suggest;

namespace Ausbeute.Service;

public sealed class LocalService(RuleStore rules, CaseStore cases, IOcr? ocr, IPdfPages? pdf, IPdfPrinter? printer, ILlmEngine? llm, string appVersion) : IService
{
    const int AutoMapMinConfidence = 60;
    const int ScanDpi = 300;
    const int PreviewDpi = 150;
    const string ProblemOffline = "Regelspeicher nicht erreichbar, es gelten die zwischengespeicherten Regeln.";
    static readonly HashSet<string> BlockingFlags = ["line_total", "sum_net", "missing_field"];

    readonly Matcher matcher = new(llm);

    public Task<StatusResp> Status(CancellationToken ct) => Guard(() =>
    {
        var rs = rules.Load();
        return new StatusResp(rules.Online, rs.Version, Format.Day(RulesDate(rs)), appVersion, rules.Online ? null : ProblemOffline);
    });

    static DateTimeOffset RulesDate(RuleSet rs) =>
        rs.Ingredients.Values.Select(e => e.Meta.ChangedAt)
            .Concat(rs.Mappings.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Products.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.YieldRules.Values.Select(e => e.Meta.ChangedAt))
            .DefaultIfEmpty(default)
            .Max();

    public Task<RuleSetResp> Rules(CancellationToken ct) => Guard(() => RulesResp(rules.Load()));

    public Task<RuleSetResp> SaveRules(RuleSet rs, CancellationToken ct) => Guard(() =>
    {
        RuleCheck.Validate(rs);
        rules.Save(rs);
        return RulesResp(rs);
    });

    static RuleSetResp RulesResp(RuleSet rs) => new(rs, Display.Rules(rs));

    public Task<ListCasesResp> ListCases(CancellationToken ct) => Guard(() => Task.FromResult(new ListCasesResp(
        cases.List().Select(c => new CaseRow(c, Display.Period(c.PeriodFrom, c.PeriodTo), Format.Day(c.UpdatedAt), c.Invoices.Count)).ToList())));

    public Task<CaseResp> GetCase(string caseId, CancellationToken ct) => Guard(() => Resp(LoadCase(caseId)));

    public Task<CaseResp> PutCase(Case kase, CancellationToken ct) => Guard(() =>
    {
        SaveCase(kase);
        return Resp(kase);
    });

    public Task DeleteCase(string caseId, CancellationToken ct) => Guard(() =>
    {
        if (caseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        cases.Delete(caseId);
        return Task.FromResult(0);
    });

    public Task<CaseResp> ImportCase(string fileName, byte[] data, CancellationToken ct) => Guard(() =>
    {
        var c = CaseStore.Decode(data);
        SaveCase(c);
        return Resp(c);
    });

    public Task<ExportResp> ExportCase(string caseId, ExportFormat format, CancellationToken ct) => Guard(() =>
    {
        var c = LoadCase(caseId);
        switch (format)
        {
            case ExportFormat.Json:
                return new ExportResp(CaseStore.Encode(c), FileName(c, "json"));
            case ExportFormat.Csv:
                var (rep, rs) = Compute(c);
                return new ExportResp(Csv.Render(c, rep, rs), FileName(c, "csv"));
            default:
                throw new ServiceError(ErrorCode.Unsupported, $"Exportformat \"{format}\" nicht unterstützt");
        }
    });

    public Task<ParseResp> ParseInvoice(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        switch (InvoiceParser.Detect(data))
        {
            case Kind.Pdf or Kind.Image:
                var empty = new Invoice();
                return new ParseResp(empty, [], Display.Invoice(empty, rules.Load()), true, null);
            case Kind.Unknown:
                throw new ServiceError(ErrorCode.Unsupported, $"Dateiformat von \"{fileName}\" nicht erkannt");
        }
        var inv = InvoiceParser.Parse(fileName, data);
        inv.Id = NewId("re-");
        var (rs, unmapped) = await MapLines(inv, true, ct);
        var c = caseId == "" ? null : Attach(caseId, inv, fileName, data);
        return new ParseResp(inv, unmapped, Display.Invoice(inv, rs), false, c);
    });

    public Task<OcrResp> OcrInvoice(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        if (llm is null) throw new ServiceError(ErrorCode.Unsupported, "Belegerkennung nicht verfügbar: kein Sprachmodell konfiguriert");
        var pages = new List<ExtractPage>();
        switch (InvoiceParser.Detect(data))
        {
            case Kind.Image:
                pages.Add(await Recognize(data, ct));
                break;
            case Kind.Pdf:
                foreach (var image in await RenderPdf(data, ScanDpi, ct) ?? throw NoPdfPages())
                    pages.Add(await Recognize(image, ct));
                break;
            default:
                throw new ServiceError(ErrorCode.Unsupported, $"\"{fileName}\" ist kein Scan");
        }
        if (pages.Count == 0) throw new ServiceError(ErrorCode.Unsupported, $"keine Seiten in \"{fileName}\" gefunden");
        var (draft, output) = await Extractor.InvoiceAsync(llm, pages, ct);
        draft.Id = NewId("re-");
        draft.FileName = fileName;
        (draft.NetTotal, draft.GrossTotal) = InvoiceMath.LineTotals(draft.Lines);
        var (rs, _) = await MapLines(draft, false, ct);
        return new OcrResp(draft.Id, output, draft, Display.Invoice(draft, rs));
    });

    async Task<ExtractPage> Recognize(byte[] image, CancellationToken ct)
    {
        if (ocr is null) throw new ServiceError(ErrorCode.Unsupported, "Texterkennung nicht verfügbar");
        ct.ThrowIfCancellationRequested();
        var page = await ocr.Recognize(image, ct);
        return new ExtractPage(page.Width, page.Height, page.Words, image);
    }

    async Task<List<byte[]>?> RenderPdf(byte[] data, int dpi, CancellationToken ct)
    {
        if (pdf is null) return null;
        try
        {
            return await pdf.Render(data, dpi, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return null;
        }
    }

    static ServiceError NoPdfPages() => new(ErrorCode.Unsupported, "PDF-Darstellung nicht verfügbar");

    public Task<VerifyResp> VerifyInvoice(VerifyReq req, CancellationToken ct) => Guard(async () =>
    {
        var inv = req.Invoice;
        if (inv.Id == "") inv.Id = NewId("re-");
        if (inv.Source == Source.Scan && inv.Verification is null)
            (inv.NetTotal, inv.GrossTotal) = InvoiceMath.LineTotals(inv.Lines);
        var flags = Check.Invoice(inv);
        var blocked = flags.Any(f => BlockingFlags.Contains(f.Code));
        var confirm = req.Confirm && !blocked;
        if ((confirm || req.Draft) && req.CaseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        var (rs, _) = await MapLines(inv, confirm, ct);
        var resp = new VerifyResp(inv, flags, Display.Invoice(inv, rs), false, null);
        if (!confirm && !req.Draft) return resp;
        if (confirm && inv.Source == Source.Scan) inv.Verification = new Verification { At = Clock.Now() };
        var c = Attach(req.CaseId, inv, req.FileName ?? inv.FileName, req.Data ?? []);
        return resp with { Invoice = inv, Case = c, Accepted = confirm };
    });

    public Task<InvoiceSourceResp> InvoiceSource(string caseId, string invoiceId, CancellationToken ct) => Guard(async () =>
    {
        string name;
        byte[] data;
        try
        {
            (name, data) = cases.LoadFile(caseId, invoiceId);
        }
        catch (CaseNotFoundException e)
        {
            throw new ServiceError(ErrorCode.NotFound, $"kein Beleg zu Rechnung {invoiceId} gespeichert", inner: e);
        }
        var pages = InvoiceParser.Detect(data) switch
        {
            Kind.Image => [new SourcePage(data, null)],
            Kind.Pdf or Kind.Zugferd =>
                (await RenderPdf(data, PreviewDpi, ct) ?? throw NoPdfPages()).Select(p => new SourcePage(p, null)).ToList(),
            _ => [new SourcePage(null, Encoding.UTF8.GetString(data))],
        };
        return new InvoiceSourceResp(name, pages);
    });

    public Task<MappingSuggestResp> SuggestMapping(InvoiceLine line, string? supplierVatId, CancellationToken ct) => Guard(async () =>
    {
        var rs = rules.Load();
        var sugs = await matcher.Suggest(rs, supplierVatId, line, ct);
        var candidates = sugs.Select(sg => new MappingCandidate(sg.Mapping, sg.Confidence, sg.Kind, Display.CandidateLabel(rs, sg.Mapping))).ToList();
        return new MappingSuggestResp(candidates, sugs.Any(sg => sg.Kind == OriginKind.Model) ? llm?.Model : null);
    });

    public Task<ReportDisplay> Calculate(string caseId, CancellationToken ct) => Guard(() =>
    {
        var c = LoadCase(caseId);
        var (rep, rs) = Compute(c);
        return Display.Report(c, rep, rs);
    });

    public Task<ReportResp> RenderReport(string caseId, bool pdf, CancellationToken ct) => Guard(async () =>
    {
        var c = LoadCase(caseId);
        var (rep, rs) = Compute(c);
        var html = Html.Render(c, rs, rep);
        if (!pdf) return new ReportResp(html, null, FileName(c, "html"));
        if (printer is null) throw new ServiceError(ErrorCode.Unsupported, "PDF-Ausgabe nicht verfügbar");
        return new ReportResp(html, await printer.Print(html, ct), FileName(c, "pdf"));
    });

    (Model.Report Report, RuleSet Rules) Compute(Case c)
    {
        var rs = rules.Load();
        try
        {
            return (Calculation.Run(c, rs), rs);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ServiceError(ErrorCode.Invalid, "Kalkulation: " + e.Message, inner: e);
        }
    }

    Case LoadCase(string id)
    {
        if (id == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        return cases.Load(id);
    }

    void SaveCase(Case c)
    {
        var t = Clock.Now();
        if (c.Id == "") c.Id = NewId("fall-");
        if (c.CreatedAt == default) c.CreatedAt = t;
        c.UpdatedAt = t;
        cases.Save(c);
    }

    CaseResp Resp(Case c) => new(c, Display.Case(c, rules.Load()));

    CaseResp Attach(string caseId, Invoice inv, string fileName, byte[] data)
    {
        var c = LoadCase(caseId);
        if (data.Length > 0) cases.SaveFile(caseId, inv.Id, fileName, data);
        var i = c.Invoices.FindIndex(x => x.Id == inv.Id);
        if (i >= 0) c.Invoices[i] = inv;
        else c.Invoices.Add(inv);
        SaveCase(c);
        return Resp(c);
    }

    async Task<(RuleSet Rules, List<int> Unmapped)> MapLines(Invoice inv, bool ask, CancellationToken ct)
    {
        var rs = rules.Load();
        var unmapped = new List<int>();
        foreach (var l in inv.Lines)
        {
            if (string.IsNullOrEmpty(l.MappingId)) rs = await MapLine(rs, inv, l, ask, ct);
            if (string.IsNullOrEmpty(l.MappingId)) unmapped.Add((int)l.No);
        }
        return (rs, unmapped);
    }

    async Task<RuleSet> MapLine(RuleSet rs, Invoice inv, InvoiceLine l, bool ask, CancellationToken ct)
    {
        if (Match.Mapping(rs, inv.SupplierVatId, inv.Date ?? Today(), l) is { } hit)
        {
            l.MappingId = hit.Id;
            return rs;
        }
        if (!ask) return rs;
        List<Suggestion> sugs;
        try
        {
            sugs = await matcher.Suggest(rs, inv.SupplierVatId, l, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return rs;
        }
        if (sugs.Count == 0) return rs;
        var sg = sugs[0];
        if (sg.Kind == OriginKind.Exact)
        {
            l.MappingId = sg.Mapping.Id;
            return rs;
        }
        if (sg.Confidence < AutoMapMinConfidence) return rs;
        var m = sg.Mapping;
        m.Id = NewId("map-");
        m.Meta = new Meta { ValidFrom = Today(), ChangedAt = Clock.Now() };
        rs.Mappings[m.Id] = m;
        try
        {
            RuleCheck.Validate(rs);
            rules.Save(rs);
        }
        catch (Exception e) when (e is RulesException or StoreUnavailableException or RulesConflictException)
        {
            return rules.Load();
        }
        l.MappingId = m.Id;
        return rs;
    }

    static string FileName(Case c, string ext)
    {
        var b = new StringBuilder();
        foreach (var r in c.Label)
        {
            switch (r)
            {
                case >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_': b.Append(r); break;
                case 'ä': b.Append('a'); break;
                case 'ö': b.Append('o'); break;
                case 'ü': b.Append('u'); break;
                case 'Ä': b.Append('A'); break;
                case 'Ö': b.Append('O'); break;
                case 'Ü': b.Append('U'); break;
                case 'ß': b.Append('s'); break;
                case ' ': b.Append('_'); break;
            }
        }
        var label = b.Length == 0 ? c.Id : b.ToString();
        return "Ausbeutekalkulation-" + label + "-" + Today().ToString("yyyy-MM-dd") + "." + ext;
    }

    static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    static string NewId(string prefix) =>
        prefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    static Task<T> Guard<T>(Func<T> body) => Guard(() => Task.FromResult(body()));

    static async Task<T> Guard<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
        }
        catch (Exception e) when (e is not (ServiceError or OperationCanceledException))
        {
            throw new ServiceError(e switch
            {
                CaseNotFoundException => ErrorCode.NotFound,
                CaseInvalidException or RulesException or InvalidDataException => ErrorCode.Invalid,
                RulesConflictException => ErrorCode.Conflict,
                StoreUnavailableException => ErrorCode.Unavailable,
                _ => ErrorCode.Internal,
            }, e.Message, inner: e);
        }
    }
}

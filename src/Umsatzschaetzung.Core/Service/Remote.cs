using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

// The services of another runtime, each call a message there and its answer a message back.
public static class Remote
{
    public static Services Over(ITransport transport)
    {
        var at = new Caller(transport);
        return new(new Rules(at), new Sammlungen(at), new Cases(at), new Invoices(at), new Assortment(at), new Mapping(at), new Reports(at));
    }

    sealed class Caller(ITransport transport)
    {
        public async Task<TResp> Call<TArg, TResp>(string method, TArg arg, CancellationToken ct) =>
            Wire.Read<TResp>(await transport.Call(method, Wire.Write(arg), ct));

        public async Task<TResp> Call<TResp>(string method, CancellationToken ct) =>
            Wire.Read<TResp>(await transport.Call(method, Message.Empty, ct));

        public async Task Send<TArg>(string method, TArg arg, CancellationToken ct)
        {
            var m = await transport.Call(method, Wire.Write(arg), ct);
            if (m.Fault is { } f) throw new ServiceError(f.Code, f.Message, f.Details);
        }
    }

    // The set crosses only when it changed since the one held here, as the local store hands out the one it keeps.
    sealed class Rules(Caller at) : IRules
    {
        RuleSet? held;

        public Task<StatusResp> Status(CancellationToken ct) => at.Call<StatusResp>("rules.status", ct);
        public async Task<RuleSet> Load(CancellationToken ct) =>
            held = await at.Call<RulesSeen?, RuleSet?>("rules.load", held is null ? null : new(held.Store, held.Version), ct) ?? held!;
        public async Task<RuleSet> Save(IRuleEntity rule, CancellationToken ct) => held = await at.Call<RuleArg, RuleSet>("rules.save", RuleArg.Of(rule), ct);
        public async Task<RuleSet> Delete(Entity kind, string id, CancellationToken ct) => held = await at.Call<DeleteRuleArg, RuleSet>("rules.delete", new(kind, id), ct);
        public async Task<RuleSet> Change(RulesChange change, CancellationToken ct) => held = await at.Call<RulesChange, RuleSet>("rules.change", change, ct);
    }

    sealed class Sammlungen(Caller at) : ISammlungen
    {
        public Task<List<SammlungInfo>> List(CancellationToken ct) => at.Call<List<SammlungInfo>>("sammlungen.list", ct);
        public Task<List<SammlungInfo>> Import(string fileName, byte[] pdf, CancellationToken ct) => at.Call<FileArg, List<SammlungInfo>>("sammlungen.import", new(fileName, pdf), ct);
        public Task<List<SammlungInfo>> Delete(int year, CancellationToken ct) => at.Call<YearArg, List<SammlungInfo>>("sammlungen.delete", new(year), ct);
    }

    sealed class Cases(Caller at) : ICases
    {
        public Task<CasesResp> List(CancellationToken ct) => at.Call<CasesResp>("cases.list", ct);
        public Task<Case> Get(string caseId, CancellationToken ct) => at.Call<CaseArg, Case>("cases.get", new(caseId), ct);
        public Task<Case> Put(Case kase, CancellationToken ct) => at.Call<Case, Case>("cases.put", kase, ct);
        public Task Delete(string caseId, CancellationToken ct) => at.Send("cases.delete", new CaseArg(caseId), ct);
        public Task<Case> Import(string fileName, byte[] data, bool overwrite, CancellationToken ct) => at.Call<CaseImportArg, Case>("cases.import", new(fileName, data, overwrite), ct);
        public Task<ExportResp> Export(string caseId, CancellationToken ct) => at.Call<CaseArg, ExportResp>("cases.export", new(caseId), ct);
    }

    sealed class Invoices(Caller at) : IInvoices
    {
        public Task<ParseResp> Parse(string caseId, string fileName, byte[] data, CancellationToken ct) => at.Call<CaseFileArg, ParseResp>("invoices.parse", new(caseId, fileName, data), ct);
        public Task<OcrResp> Ocr(string caseId, string fileName, byte[] data, CancellationToken ct) => at.Call<CaseFileArg, OcrResp>("invoices.ocr", new(caseId, fileName, data), ct);
        public Task<VerifyResp> Verify(VerifyReq req, CancellationToken ct) => at.Call<VerifyReq, VerifyResp>("invoices.verify", req, ct);
        public Task<Case> Delete(string caseId, string invoiceId, CancellationToken ct) => at.Call<InvoiceArg, Case>("invoices.delete", new(caseId, invoiceId), ct);
        public Task<ExportResp> Export(string caseId, string invoiceId, CancellationToken ct) => at.Call<InvoiceArg, ExportResp>("invoices.export", new(caseId, invoiceId), ct);
        public Task<InvoiceSourceResp> Source(string caseId, string invoiceId, CancellationToken ct) => at.Call<InvoiceArg, InvoiceSourceResp>("invoices.source", new(caseId, invoiceId), ct);
        public Task<InvoiceReadingResp> Reading(string caseId, string invoiceId, CancellationToken ct) => at.Call<InvoiceArg, InvoiceReadingResp>("invoices.reading", new(caseId, invoiceId), ct);
        public Task<Raster?> Snippet(string caseId, string invoiceId, int line, string name, CancellationToken ct) => at.Call<SnippetArg, Raster?>("invoices.snippet", new(caseId, invoiceId, line, name), ct);
        public Task<Case> UnifySuppliers(string caseId, List<string> invoiceIds, CancellationToken ct) => at.Call<UnifyArg, Case>("invoices.unify", new(caseId, invoiceIds), ct);
    }

    sealed class Assortment(Caller at) : IAssortment
    {
        public Task<ExportResp> Export(string caseId, CancellationToken ct) => at.Call<CaseArg, ExportResp>("assortment.export", new(caseId), ct);
        public Task<AssortmentImport> Read(byte[] data, CancellationToken ct) => at.Call<DataArg, AssortmentImport>("assortment.read", new(data), ct);
        public Task<List<string>> Suggest(string caseId, List<string> dismissed, CancellationToken ct) => at.Call<DismissedArg, List<string>>("assortment.suggest", new(caseId, dismissed), ct);
    }

    sealed class Mapping(Caller at) : IMapping
    {
        public Task<List<MappingCandidate>> Suggest(string caseId, InvoiceLine line, string? supplier, CancellationToken ct) => at.Call<SuggestArg, List<MappingCandidate>>("mapping.suggest", new(caseId, line, supplier), ct);
        public Task<Case> Map(string caseId, CancellationToken ct) => at.Call<CaseArg, Case>("mapping.map", new(caseId), ct);
        public Task Warm(string caseId, CancellationToken ct) => at.Send("mapping.warm", new CaseArg(caseId), ct);
    }

    sealed class Reports(Caller at) : IReports
    {
        public Task<CalcResp> Calculate(string caseId, CancellationToken ct) => at.Call<CaseArg, CalcResp>("reports.calculate", new(caseId), ct);
        public Task<ReportResp> Render(string caseId, bool pdf, CancellationToken ct) => at.Call<RenderArg, ReportResp>("reports.render", new(caseId, pdf), ct);
    }
}

// The serving side: a message names the call, the services answer it, and what they refuse
// travels back as a fault.
public sealed class Dispatch(Services s)
{
    public async Task<Message> Handle(string method, Message m, CancellationToken ct)
    {
        try
        {
            return await Answer(method, m, ct);
        }
        catch (ServiceError e)
        {
            return Wire.Fail(e);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return Wire.Fail(new ServiceError(ErrorCode.Internal, e.Message, inner: e));
        }
    }

    Task<Message> Answer(string method, Message m, CancellationToken ct) => method switch
    {
        "rules.status" => Out(s.Rules.Status(ct)),
        "rules.load" => In<RulesSeen?, RuleSet?>(m, async seen => await s.Rules.Load(ct) is var rs && seen == new RulesSeen(rs.Store, rs.Version) ? null : rs),
        "rules.save" => Out(s.Rules.Save(Wire.Read<RuleArg>(m).Rule, ct)),
        "rules.delete" => In<DeleteRuleArg, RuleSet>(m, a => s.Rules.Delete(a.Kind, a.Id, ct)),
        "rules.change" => In<RulesChange, RuleSet>(m, a => s.Rules.Change(a, ct)),

        "sammlungen.list" => Out(s.Sammlungen.List(ct)),
        "sammlungen.import" => In<FileArg, List<SammlungInfo>>(m, a => s.Sammlungen.Import(a.FileName, a.Data, ct)),
        "sammlungen.delete" => In<YearArg, List<SammlungInfo>>(m, a => s.Sammlungen.Delete(a.Year, ct)),

        "cases.list" => Out(s.Cases.List(ct)),
        "cases.get" => In<CaseArg, Case>(m, a => s.Cases.Get(a.CaseId, ct)),
        "cases.put" => In<Case, Case>(m, a => s.Cases.Put(a, ct)),
        "cases.delete" => Done(s.Cases.Delete(Wire.Read<CaseArg>(m).CaseId, ct)),
        "cases.import" => In<CaseImportArg, Case>(m, a => s.Cases.Import(a.FileName, a.Data, a.Overwrite, ct)),
        "cases.export" => In<CaseArg, ExportResp>(m, a => s.Cases.Export(a.CaseId, ct)),

        "invoices.parse" => In<CaseFileArg, ParseResp>(m, a => s.Invoices.Parse(a.CaseId, a.FileName, a.Data, ct)),
        "invoices.ocr" => In<CaseFileArg, OcrResp>(m, a => s.Invoices.Ocr(a.CaseId, a.FileName, a.Data, ct)),
        "invoices.verify" => In<VerifyReq, VerifyResp>(m, a => s.Invoices.Verify(a, ct)),
        "invoices.delete" => In<InvoiceArg, Case>(m, a => s.Invoices.Delete(a.CaseId, a.InvoiceId, ct)),
        "invoices.export" => In<InvoiceArg, ExportResp>(m, a => s.Invoices.Export(a.CaseId, a.InvoiceId, ct)),
        "invoices.source" => In<InvoiceArg, InvoiceSourceResp>(m, a => s.Invoices.Source(a.CaseId, a.InvoiceId, ct)),
        "invoices.reading" => In<InvoiceArg, InvoiceReadingResp>(m, a => s.Invoices.Reading(a.CaseId, a.InvoiceId, ct)),
        "invoices.snippet" => In<SnippetArg, Raster?>(m, a => s.Invoices.Snippet(a.CaseId, a.InvoiceId, a.Line, a.Name, ct)),
        "invoices.unify" => In<UnifyArg, Case>(m, a => s.Invoices.UnifySuppliers(a.CaseId, a.InvoiceIds, ct)),

        "assortment.export" => In<CaseArg, ExportResp>(m, a => s.Assortment.Export(a.CaseId, ct)),
        "assortment.read" => In<DataArg, AssortmentImport>(m, a => s.Assortment.Read(a.Data, ct)),
        "assortment.suggest" => In<DismissedArg, List<string>>(m, a => s.Assortment.Suggest(a.CaseId, a.Dismissed, ct)),

        "mapping.suggest" => In<SuggestArg, List<MappingCandidate>>(m, a => s.Mapping.Suggest(a.CaseId, a.Line, a.Supplier, ct)),
        "mapping.map" => In<CaseArg, Case>(m, a => s.Mapping.Map(a.CaseId, ct)),
        "mapping.warm" => Done(s.Mapping.Warm(Wire.Read<CaseArg>(m).CaseId, ct)),

        "reports.calculate" => In<CaseArg, CalcResp>(m, a => s.Reports.Calculate(a.CaseId, ct)),
        "reports.render" => In<RenderArg, ReportResp>(m, a => s.Reports.Render(a.CaseId, a.Pdf, ct)),

        _ => throw new ServiceError(ErrorCode.NotFound, $"Unbekannter Aufruf: {method}"),
    };

    static async Task<Message> In<TArg, TResp>(Message m, Func<TArg, Task<TResp>> call) => Wire.Write(await call(Wire.Read<TArg>(m)));

    static async Task<Message> Out<TResp>(Task<TResp> call) => Wire.Write(await call);

    static async Task<Message> Done(Task call)
    {
        await call;
        return Message.Empty;
    }
}

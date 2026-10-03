using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Service;

// What the program asks of its data, one part per concern, so a head can hand in another part
// for one of them: `Services.Local(...) with { Mapping = ... }`.
public sealed record Services(IRules Rules, ISammlungen Sammlungen, ICases Cases, IInvoices Invoices, IAssortment Assortment, IMapping Mapping, IReports Reports)
{
    public static Services Local(RuleStore rules, CaseStore cases, string appVersion,
        IDocuments? documents = null, ITagger? tagger = null, IRanking? ranking = null, IPdfPrinter? printer = null, Readings? readings = null)
    {
        var own = new LocalCases(rules, cases);
        var mapping = new LocalMapping(rules, own, ranking);
        return new(
            new LocalRules(rules, appVersion),
            new LocalSammlungen(rules, documents),
            own,
            new LocalInvoices(rules, own, mapping, documents, tagger, readings),
            new LocalAssortment(rules, own),
            mapping,
            new LocalReports(rules, own, documents, printer, appVersion));
    }
}

public interface IRules
{
    Task<StatusResp> Status(CancellationToken ct);                                                                          // GET  /status
    Task<RuleSet> Load(CancellationToken ct);                                                                               // GET  /rules
    Task<RuleSet> Save(IRuleEntity rule, CancellationToken ct);                                                             // PUT  /rules/{kind}/{id}   last write wins, resp holds the whole set
    Task<RuleSet> Delete(Entity kind, string id, CancellationToken ct);                                                     // DELETE /rules/{kind}/{id}   refused while other entries reference it, resp holds the whole set
    Task<RuleSet> Change(RulesChange change, CancellationToken ct);                                                         // POST /rules/change   all or nothing, checked as the set it leaves
}

public interface ISammlungen
{
    Task<List<SammlungInfo>> List(CancellationToken ct);                                                                    // GET    /richtsatz
    Task<List<SammlungInfo>> Import(string fileName, byte[] pdf, CancellationToken ct);                                     // POST   /richtsatz/import   liest die PDF und ersetzt die Sammlung ihres Jahres
    Task<List<SammlungInfo>> Delete(int year, CancellationToken ct);                                                        // DELETE /richtsatz/{year}   eine mitgelieferte Sammlung kehrt beim nächsten Start zurück
}

public interface ICases
{
    Task<CasesResp> List(CancellationToken ct);                                                                             // GET    /cases
    Task<Case> Get(string caseId, CancellationToken ct);                                                                    // GET    /cases/{id}
    Task<Case> Put(Case kase, CancellationToken ct);                                                                        // PUT    /cases/{id}
    Task Delete(string caseId, CancellationToken ct);                                                                       // DELETE /cases/{id}
    Task<Case> Import(string fileName, byte[] data, bool overwrite, CancellationToken ct);                                  // POST   /cases/import   ohne overwrite meldet ein Fall mit derselben ID oder Bezeichnung Conflict, Details trägt die Bezeichnungen
    Task<ExportResp> Export(string caseId, CancellationToken ct);                                                           // GET    /cases/{id}/export   a copy of the case file
}

public interface IInvoices
{
    Task<ParseResp> Parse(string caseId, string fileName, byte[] data, CancellationToken ct);                               // POST /cases/{id}/invoices/parse   persists the invoice; resp.Case is the saved case
    Task<OcrResp> Ocr(string caseId, string fileName, byte[] data, CancellationToken ct);                                   // POST /cases/{id}/invoices/ocr     draft only, nothing is saved
    Task<VerifyResp> Verify(VerifyReq req, CancellationToken ct);                                                           // POST /cases/{id}/invoices/{inv}/verify   see Intent
    Task<Case> Delete(string caseId, string invoiceId, CancellationToken ct);                                               // DELETE /cases/{id}/invoices/{inv}   drops the invoice, its stored file stays until CaseStore.Purge, resp is the saved case
    Task<ExportResp> Export(string caseId, string invoiceId, CancellationToken ct);                                         // GET  /cases/{id}/invoices/{inv}/export   the read invoice as CSV
    Task<InvoiceSourceResp> Source(string caseId, string invoiceId, CancellationToken ct);                                  // GET  /cases/{id}/invoices/{inv}/source
    Task<InvoiceReadingResp> Reading(string caseId, string invoiceId, CancellationToken ct);                                // GET  /cases/{id}/invoices/{inv}/reading   pages of the stored reading with the pages kept
    Task<Raster?> Snippet(string caseId, string invoiceId, int line, string name, CancellationToken ct);                    // GET  /cases/{id}/invoices/{inv}/lines/{n}/snippet?name=   the row of the scan the line was read from
    Task<Case> UnifySuppliers(string caseId, List<string> invoiceIds, CancellationToken ct);                                // POST /cases/{id}/suppliers/unify    renames only the given invoices, see Suppliers.Unify
}

public interface IAssortment
{
    Task<ExportResp> Export(string caseId, CancellationToken ct);                                                           // GET  /cases/{id}/assortment/export   the listed products as CSV
    Task<AssortmentImport> Read(byte[] data, CancellationToken ct);                                                         // POST /assortment/read   the products of the CSV with their price, nothing is saved; resp.Unknown names the rows no product matched
    Task<List<string>> Suggest(string caseId, List<string> dismissed, CancellationToken ct);                                // POST /cases/{id}/assortment/suggest   the ids of catalog products worth listing, best first
}

public interface IMapping
{
    Task<List<MappingCandidate>> Suggest(string caseId, InvoiceLine line, string? supplier, CancellationToken ct);          // POST /cases/{id}/mappings/suggest   caseId "" suggests without a Gewerbe filter
    Task<Case> Map(string caseId, CancellationToken ct);                                                                    // POST /cases/{id}/mappings/run       maps every open line the matcher is sure about
    Task Warm(string caseId, CancellationToken ct);                                                                         // POST /cases/{id}/mappings/warm      readies the matcher for the case's Gewerbe
}

public interface IReports
{
    Task<CalcResp> Calculate(string caseId, CancellationToken ct);                                                          // POST /cases/{id}/calc
    Task<ReportResp> Render(string caseId, bool pdf, CancellationToken ct);                                                 // POST /cases/{id}/report
}

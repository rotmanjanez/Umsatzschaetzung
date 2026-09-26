using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

public interface IService
{
    Task<StatusResp> Status(CancellationToken ct);                                                                          // GET  /status
    Task<RuleSet> Rules(CancellationToken ct);                                                                              // GET  /rules
    Task<RuleSet> SaveRule(IRuleEntity rule, CancellationToken ct);                                                         // PUT  /rules/{kind}/{id}   last write wins, resp holds the whole set
    Task<RuleSet> DeleteRule(Entity kind, string id, CancellationToken ct);                                                 // DELETE /rules/{kind}/{id}   refused while other entries reference it, resp holds the whole set

    Task<List<SammlungInfo>> Sammlungen(CancellationToken ct);                                                              // GET    /richtsatz
    Task<List<SammlungInfo>> ImportSammlung(string fileName, byte[] pdf, CancellationToken ct);                             // POST   /richtsatz/import   liest die PDF und ersetzt die Sammlung ihres Jahres
    Task<List<SammlungInfo>> DeleteSammlung(int year, CancellationToken ct);                                                // DELETE /richtsatz/{year}   eine mitgelieferte Sammlung kehrt beim nächsten Start zurück

    Task<CasesResp> ListCases(CancellationToken ct);                                                                        // GET    /cases
    Task<Case> GetCase(string caseId, CancellationToken ct);                                                                // GET    /cases/{id}
    Task<Case> PutCase(Case kase, CancellationToken ct);                                                                    // PUT    /cases/{id}
    Task DeleteCase(string caseId, CancellationToken ct);                                                                   // DELETE /cases/{id}
    Task<Case> ImportCase(string fileName, byte[] data, bool overwrite, CancellationToken ct);                              // POST   /cases/import   ohne overwrite meldet ein Fall mit derselben ID oder Bezeichnung Conflict, Details trägt die Bezeichnungen
    Task<ExportResp> ExportCase(string caseId, CancellationToken ct);                                                       // GET    /cases/{id}/export   a copy of the case file

    Task<ParseResp> ParseInvoice(string caseId, string fileName, byte[] data, CancellationToken ct);                        // POST /cases/{id}/invoices/parse   persists the invoice; resp.Case is the saved case
    Task<OcrResp> OcrInvoice(string caseId, string fileName, byte[] data, CancellationToken ct);                            // POST /cases/{id}/invoices/ocr     draft only, nothing is saved
    Task<VerifyResp> VerifyInvoice(VerifyReq req, CancellationToken ct);                                                    // POST /cases/{id}/invoices/{inv}/verify   see Intent
    Task<Case> DeleteInvoice(string caseId, string invoiceId, CancellationToken ct);                                        // DELETE /cases/{id}/invoices/{inv}   drops the invoice, its stored file stays until CaseStore.Purge, resp is the saved case
    Task<ExportResp> ExportInvoice(string caseId, string invoiceId, CancellationToken ct);                                  // GET  /cases/{id}/invoices/{inv}/export   the read invoice as CSV
    Task<InvoiceSourceResp> InvoiceSource(string caseId, string invoiceId, CancellationToken ct);                           // GET  /cases/{id}/invoices/{inv}/source
    Task<InvoiceReadingResp> InvoiceReading(string caseId, string invoiceId, CancellationToken ct);                         // GET  /cases/{id}/invoices/{inv}/reading   pages of the stored reading, images rendered again
    Task<Raster?> InvoiceSnippet(string caseId, string invoiceId, int line, string name, CancellationToken ct);            // GET  /cases/{id}/invoices/{inv}/lines/{n}/snippet?name=   the row of the scan the line was read from
    Task<ExportResp> ExportAssortment(string caseId, CancellationToken ct);                                                 // GET  /cases/{id}/assortment/export   the listed products as CSV
    Task<AssortmentImport> ReadAssortment(byte[] data, CancellationToken ct);                                               // POST /assortment/read   the products of the CSV with their price, nothing is saved; resp.Unknown names the rows no product matched
    Task<List<MappingCandidate>> SuggestMapping(string caseId, InvoiceLine line, string? supplier, CancellationToken ct);   // POST /cases/{id}/mappings/suggest   caseId "" suggests without a Gewerbe filter
    Task<Case> MapCase(string caseId, CancellationToken ct);                                                                // POST /cases/{id}/mappings/run       maps every open line the matcher is sure about
    Task<Case> UnifySuppliers(string caseId, List<string> invoiceIds, CancellationToken ct);                                // POST /cases/{id}/suppliers/unify    renames only the given invoices, see Suppliers.Unify

    Task<CalcResp> Calculate(string caseId, CancellationToken ct);                                                          // POST /cases/{id}/calc
    Task<ReportResp> RenderReport(string caseId, bool pdf, CancellationToken ct);                                           // POST /cases/{id}/report
}

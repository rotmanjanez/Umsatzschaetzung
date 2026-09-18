using Umsatzschätzung.Model;

namespace Umsatzschätzung.Service;

public interface IService
{
    Task<StatusResp> Status(CancellationToken ct);                                                          // GET  /status
    Task<RuleSetResp> Rules(CancellationToken ct);                                                          // GET  /rules
    Task<RuleSetResp> SaveRule(IRuleEntity rule, CancellationToken ct);                                     // PUT  /rules/{kind}/{id}   last write wins, resp holds the whole set
    Task<RuleSetResp> DeleteRule(Entity kind, string id, CancellationToken ct);                             // DELETE /rules/{kind}/{id}   refused while other entries reference it, resp holds the whole set

    Task<ListCasesResp> ListCases(CancellationToken ct);                                                    // GET    /cases
    Task<CaseResp> GetCase(string caseId, CancellationToken ct);                                            // GET    /cases/{id}
    Task<CaseResp> PutCase(Case kase, CancellationToken ct);                                                // PUT    /cases/{id}
    Task DeleteCase(string caseId, CancellationToken ct);                                                   // DELETE /cases/{id}
    Task<CaseResp> ImportCase(string fileName, byte[] data, CancellationToken ct);                          // POST   /cases/import
    Task<ExportResp> ExportCase(string caseId, ExportFormat format, CancellationToken ct);                  // GET    /cases/{id}/export?format=

    Task<ParseResp> ParseInvoice(string caseId, string fileName, byte[] data, CancellationToken ct);        // POST /cases/{id}/invoices/parse   persists the invoice; resp.Case is the saved case
    Task<OcrResp> OcrInvoice(string caseId, string fileName, byte[] data, CancellationToken ct);            // POST /cases/{id}/invoices/ocr     draft only, nothing is saved
    Task<VerifyResp> VerifyInvoice(VerifyReq req, CancellationToken ct);                                    // POST /cases/{id}/invoices/{inv}/verify   Confirm without blocking flag persists as verified, Draft persists unverified
    Task<CaseResp> DeleteInvoice(string caseId, string invoiceId, CancellationToken ct);                    // DELETE /cases/{id}/invoices/{inv}   drops the invoice and its stored file, resp is the saved case
    Task<InvoiceSourceResp> InvoiceSource(string caseId, string invoiceId, CancellationToken ct);           // GET  /cases/{id}/invoices/{inv}/source
    Task<MappingSuggestResp> SuggestMapping(string caseId, InvoiceLine line, string? supplier, CancellationToken ct); // POST /cases/{id}/mappings/suggest   caseId "" suggests without a Gewerbe filter

    Task<ReportDisplay> Calculate(string caseId, CancellationToken ct);                                     // POST /cases/{id}/calc
    Task<ReportResp> RenderReport(string caseId, bool pdf, CancellationToken ct);                           // POST /cases/{id}/report
}

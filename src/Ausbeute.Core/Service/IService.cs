using Ausbeute.Model;

namespace Ausbeute.Service;

public interface IService
{
    Task<StatusResp> Status(CancellationToken ct);                                                          // GET  /status
    Task<RuleSetResp> Rules(CancellationToken ct);                                                          // GET  /rules
    Task<ChangesResp> RulesChanges(long since, CancellationToken ct);                                       // GET  /rules/changes?since=
    Task<ApplyChangesResp> ApplyChanges(long baseVersion, List<Change> changes, CancellationToken ct);      // POST /rules/changes

    Task<ListCasesResp> ListCases(CancellationToken ct);                                                    // GET    /cases
    Task<CaseResp> GetCase(string caseId, CancellationToken ct);                                            // GET    /cases/{id}
    Task<CaseResp> PutCase(Case kase, CancellationToken ct);                                                // PUT    /cases/{id}
    Task DeleteCase(string caseId, CancellationToken ct);                                                   // DELETE /cases/{id}
    Task<CaseResp> ImportCase(string fileName, byte[] data, CancellationToken ct);                          // POST   /cases/import
    Task<ExportResp> ExportCase(string caseId, ExportFormat format, CancellationToken ct);                  // GET    /cases/{id}/export?format=

    Task<ParseResp> ParseInvoice(string caseId, string fileName, byte[] data, CancellationToken ct);        // POST /cases/{id}/invoices/parse   persists the invoice; resp.Case is the saved case
    Task<OcrResp> OcrInvoice(string caseId, string fileName, byte[] data, CancellationToken ct);            // POST /cases/{id}/invoices/ocr     draft only, nothing is saved
    Task<VerifyResp> VerifyInvoice(VerifyReq req, CancellationToken ct);                                    // POST /cases/{id}/invoices/{inv}/verify   Confirm without blocking flag persists as verified, Draft persists unverified
    Task<InvoiceSourceResp> InvoiceSource(string caseId, string invoiceId, CancellationToken ct);           // GET  /cases/{id}/invoices/{inv}/source
    Task<MappingSuggestResp> SuggestMapping(InvoiceLine line, string? supplierVatId, CancellationToken ct); // POST /mappings/suggest

    Task<ReportDisplay> Calculate(string caseId, CancellationToken ct);                                     // POST /cases/{id}/calc
    Task<ReportResp> RenderReport(string caseId, bool pdf, CancellationToken ct);                           // POST /cases/{id}/report
}

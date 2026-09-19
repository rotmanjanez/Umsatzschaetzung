using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

public sealed record StatusResp(long RulesVersion, string RulesDate, string AppVersion, string? Problem);

public sealed record RuleSetResp(RuleSet RuleSet, RuleSetDisplay Display);

public sealed record RuleSetDisplay(Dictionary<string, ProductDisplay> Products, Dictionary<string, string> Categories);

public sealed record ProductDisplay(string Recipe);

public sealed record SammlungenResp(List<SammlungInfo> Sammlungen, List<SammlungRow> Display);

public sealed record SammlungRow(int Year, string Klassen, string Quelle, string Importiert);

public sealed record ListCasesResp(List<CaseRow> Cases);

public sealed record CaseRow(Case Case, string Period, string UpdatedAt, int Invoices);

public sealed record CaseResp(Case Case, CaseDisplay Display);

public sealed record CaseDisplay(string Period, string PeriodFrom, string PeriodTo, Dictionary<long, string> Declared, Dictionary<string, InvoiceDisplay> Invoices);

public sealed record ParseResp(Invoice Invoice, List<int> UnmappedLines, InvoiceDisplay Display, bool NeedsOcr, CaseResp? Case);

public sealed record InvoiceDisplay(string Date, string NetTotal, string GrossTotal, List<LineDisplay> Lines);

public sealed record LineDisplay(string Quantity, string UnitPrice, string LineNet, string Vat, string Mapping);

public sealed record OcrResp(string InvoiceId, List<OcrPage> Pages, Invoice Draft, InvoiceDisplay Display);

// Check looks without storing, Store keeps the invoice for review, Confirm takes it over unless a
// flag blocks it, and Auto takes it over only if the reading is complete and adds up on its own.
public enum Intent { Check, Store, Confirm, Auto }

// Reading is kept with the invoice when the intent stores it; the page images are left out of what
// is written, they are rendered from the document again.
public sealed record VerifyReq(string CaseId, Invoice Invoice, Intent Intent, string? FileName, byte[]? Data, List<OcrPage>? Reading = null);

public sealed record VerifyResp(Invoice Invoice, List<Flag> Flags, bool Blocked, InvoiceDisplay Display, bool Accepted, CaseResp? Case);

public sealed record SourcePage(byte[]? Image, string? Text);

public sealed record InvoiceSourceResp(string FileName, List<SourcePage> Pages);

// Empty when the invoice was never read from a scan.
public sealed record InvoiceReadingResp(List<OcrPage> Pages);

public sealed record MappingSuggestResp(List<MappingCandidate> Candidates);

public sealed record MappingCandidate(ArticleMapping Mapping, int Confidence, OriginKind Kind, string Display);

public sealed record ReportDisplay(List<KV> Summary, List<ProductRowDisplay> Products, List<RevenueRow> Revenue, NodeDisplay Root, ExcludedDisplay Excluded);

public sealed record RevenueRow(string Vat, string Declared, string Calculated, string Difference, bool Total);

public sealed record ExcludedDisplay(string Purchases, string Included, string Excluded, string Share, List<ExcludedRow> Unmapped, List<ExcludedRow> Unused);

public sealed record ExcludedRow(string Invoice, long LineNo, string Name, string Ingredient, string LineNet);

public sealed record ProductRowDisplay(string ProductId, string Name, string Recipe, string Portions, string Count, bool Pinned, string GrossPrice, string Vat, string Revenue, bool Disabled, bool PriceMissing);

public sealed record KV(string Key, string Label, string Value);

public sealed record NodeDisplay(string Label, string Value, string? Formula, List<string> Sources, List<NodeDisplay> Inputs);

public sealed record ReportResp(string Html, byte[]? Pdf, string FileName);

public enum ExportFormat { Case, Csv }

public sealed record ExportResp(byte[] Data, string FileName);


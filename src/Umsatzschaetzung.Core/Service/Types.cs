using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

public sealed record StatusResp(long RulesVersion, DateTimeOffset RulesDate, string AppVersion, string? Problem);

// Files in the case folder that hold no case, by name; the cases are listed without them.
public sealed record CasesResp(List<Case> Cases, List<string> Unreadable);

public sealed record ParseResp(Invoice Invoice, List<int> UnmappedLines, bool NeedsOcr, Case? Case);

public sealed record OcrResp(string InvoiceId, List<OcrPage> Pages, Invoice Draft);

// Check looks without storing, Store keeps the invoice for review, Confirm takes it over unless a
// flag blocks it, and Auto takes it over only if the reading is complete and adds up on its own.
public enum Intent { Check, Store, Confirm, Auto }

// Reading is kept with the invoice when the intent stores it; the page images are left out of what
// is written, they are rendered from the document again.
public sealed record VerifyReq(string CaseId, Invoice Invoice, Intent Intent, string? FileName, byte[]? Data, List<OcrPage>? Reading = null);

public sealed record VerifyResp(Invoice Invoice, List<Flag> Flags, bool Blocked, bool Accepted, Case? Case);

public sealed record SourcePage(Raster? Image, string? Text);

public sealed record InvoiceSourceResp(string FileName, List<SourcePage> Pages);

// Empty when the invoice was never read from a scan.
public sealed record InvoiceReadingResp(List<OcrPage> Pages);

public sealed record MappingCandidate(ArticleMapping Mapping, int Confidence, OriginKind Kind);

// Die Rechnung und der Rahmensatz, an dem sie zu messen ist: beides roh, die Darstellung
// entsteht erst im Bedienteil.
public sealed record CalcResp(Report Report, Rahmen? Rahmen);

public sealed record ReportResp(string Html, byte[]? Pdf, string? FileName);

public sealed record ExportResp(byte[] Data, string FileName);

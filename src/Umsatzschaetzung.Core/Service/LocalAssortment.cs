using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Rulestore;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

sealed class LocalAssortment(RuleStore rules, LocalCases cases) : IAssortment
{
    public Task<ExportResp> Export(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = cases.Load(caseId);
        return new ExportResp(Csv.Assortment(c, rules.Load()), FileName(c.Label + " Sortiment", "csv"));
    });

    public Task<AssortmentImport> Read(byte[] data, CancellationToken ct) => Guard(ct, () => Csv.ReadAssortment(data, rules.Load()));

    public Task<List<string>> Suggest(string caseId, List<string> dismissed, CancellationToken ct) =>
        Guard(ct, () =>
        {
            var c = cases.Load(caseId);
            var rs = rules.Load();
            try
            {
                return Suggestions.For(c, rs, dismissed.ToHashSet());
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                throw new ServiceError(ErrorCode.Invalid, "Kalkulation: " + e.Message, inner: e);
            }
        });
}

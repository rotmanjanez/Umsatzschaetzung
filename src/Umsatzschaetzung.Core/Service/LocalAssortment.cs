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
}

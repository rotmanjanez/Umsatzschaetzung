using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Rulestore;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

sealed class LocalSammlungen(RuleStore rules, IDocuments? documents) : ISammlungen
{
    public Task<List<SammlungInfo>> List(CancellationToken ct) => Guard(ct, rules.Sammlungen);

    public Task<List<SammlungInfo>> Import(string fileName, byte[] pdf, CancellationToken ct) => Guard(ct, () =>
    {
        if (pdf.Length == 0) throw new ServiceError(ErrorCode.Invalid, "Leere Datei");
        if (documents is null) throw new ServiceError(ErrorCode.Unsupported, "Belege lassen sich hier nicht lesen");
        return rules.ImportSammlung(Richtsätze.Read(documents.Sheets(pdf)), fileName);
    });

    public Task<List<SammlungInfo>> Delete(int year, CancellationToken ct) => Guard(ct, () =>
    {
        if (!rules.Sammlungen().Exists(s => s.Year == year)) throw new ServiceError(ErrorCode.NotFound, $"Richtsatzsammlung {year}");
        return rules.DeleteSammlung(year);
    });
}

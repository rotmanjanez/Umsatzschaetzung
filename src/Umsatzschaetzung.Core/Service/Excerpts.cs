using System.Collections.Concurrent;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

// Rows of an invoice cut from the pages kept beside its reading; the reading is read once from the
// case. A row costs a small page and a crop, not the scan.
sealed class Excerpts(CaseStore cases)
{
    const int Most = 256;

    readonly ConcurrentDictionary<(string Case, string Invoice), Lazy<Task<List<OcrPage>?>>> readings = new();

    public async Task<Raster?> Row(IDocuments documents, string caseId, string invoiceId, int line, string name)
    {
        if (await Of(caseId, invoiceId) is not { } pages || Rows.Of(pages, line, name) is not (var at, var box)) return null;
        return cases.LoadImages(caseId, invoiceId, at) is [var kept] ? documents.Cut(kept, box) : null;
    }

    public void Forget(string caseId, string invoiceId) => readings.TryRemove((caseId, invoiceId), out _);

    public void Forget(string caseId)
    {
        foreach (var key in readings.Keys)
            if (key.Case == caseId) readings.TryRemove(key, out _);
    }

    // Asked for together, read once; a read that failed is tried again by the next one to ask.
    Task<List<OcrPage>?> Of(string caseId, string invoiceId)
    {
        if (readings.Count > Most) readings.Clear();
        var key = (caseId, invoiceId);
        Lazy<Task<List<OcrPage>?>>? mine = null;
        mine = new(async () =>
        {
            try
            {
                return await Task.Run(() => cases.LoadReading(caseId, invoiceId));
            }
            catch
            {
                readings.TryRemove(KeyValuePair.Create(key, mine!));
                throw;
            }
        });
        return readings.GetOrAdd(key, mine).Value;
    }
}

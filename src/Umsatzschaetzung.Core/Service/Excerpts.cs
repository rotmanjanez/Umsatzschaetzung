using System.Collections.Concurrent;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Service;

// Rows of an invoice cut from what is kept of it: its reading read once from the case, each page
// rendered once and kept small. Until its document changes, a row costs a crop, not a trip to the
// case file and a decode of the whole scan.
sealed class Excerpts(CaseStore cases)
{
    const int Most = 256;

    sealed record Source(List<OcrPage> Pages, ConcurrentDictionary<int, Lazy<Task<byte[]>>> Kept);

    readonly ConcurrentDictionary<(string Case, string Invoice), Lazy<Task<Source?>>> sources = new();

    public async Task<Raster?> Row(IDocuments documents, string caseId, string invoiceId, int line, string name)
    {
        if (await Of(caseId, invoiceId) is not { } source || Rows.Of(source.Pages, line, name) is not (var at, var box)) return null;
        byte[] page;
        try
        {
            page = await Once(source.Kept, at, () => Task.Run(() =>
            {
                var (_, data) = cases.LoadFile(caseId, invoiceId);
                return documents.Keep(data, at, source.Pages[at].Correction, CancellationToken.None);
            }));
        }
        catch (CaseNotFoundException)
        {
            return null;
        }
        return documents.Cut(page, box);
    }

    public void Forget(string caseId, string invoiceId) => sources.TryRemove((caseId, invoiceId), out _);

    public void Forget(string caseId)
    {
        foreach (var key in sources.Keys)
            if (key.Case == caseId) sources.TryRemove(key, out _);
    }

    Task<Source?> Of(string caseId, string invoiceId)
    {
        if (sources.Count > Most) sources.Clear();
        return Once(sources, (caseId, invoiceId), () => Task.Run(() =>
            cases.LoadReading(caseId, invoiceId) is { } pages ? new Source(pages, new()) : null));
    }

    // Asked for together, loaded once; a load that failed is tried again by the next one to ask.
    static Task<T> Once<TKey, T>(ConcurrentDictionary<TKey, Lazy<Task<T>>> map, TKey key, Func<Task<T>> load) where TKey : notnull
    {
        Lazy<Task<T>>? mine = null;
        mine = new Lazy<Task<T>>(async () =>
        {
            try
            {
                return await load();
            }
            catch
            {
                map.TryRemove(KeyValuePair.Create(key, mine!));
                throw;
            }
        });
        return map.GetOrAdd(key, mine).Value;
    }
}

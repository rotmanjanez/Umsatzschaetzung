using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Extract;

public static class Extractor
{
    public static Task<Invoice> InvoiceAsync(Tagger tagger, List<OcrPage> pages, CancellationToken ct) => Task.Run(() =>
    {
        var tagged = new List<List<TaggedWord>>(pages.Count);
        foreach (var p in pages)
        {
            ct.ThrowIfCancellationRequested();
            tagged.Add(tagger.Tag(p.Words, p.Width, p.Height));
        }
        var inv = Assemble.Invoice(tagged, pages);
        Distribute(pages, Check.Invoice(inv));
        return inv;
    }, ct);

    static void Distribute(List<OcrPage> pages, List<Flag> flags)
    {
        if (pages.Count == 0) return;
        foreach (var f in flags)
        {
            var lines = pages.SelectMany(p => p.Lines).Where(l => f.LineNo != 0 && l.Parsed.No == f.LineNo).ToList();
            if (lines.Count == 0) pages[0].Flags.Add(f);
            foreach (var l in lines) l.Flags.Add(f);
        }
    }
}

using Umsatzschätzung.Model;
using Umsatzschätzung.Tagging;

namespace Umsatzschätzung.Extract;

public sealed record ExtractPage(int Width, int Height, List<OcrWord> Words, byte[] Image);

public static class Extractor
{
    public static Task<(Invoice Invoice, List<OcrPage> Pages)> InvoiceAsync(Tagger tagger, IReadOnlyList<ExtractPage> pages, CancellationToken ct) => Task.Run(() =>
    {
        var output = new List<OcrPage>(pages.Count);
        var tagged = new List<List<TaggedWord>>(pages.Count);
        foreach (var p in pages)
        {
            ct.ThrowIfCancellationRequested();
            output.Add(new OcrPage { Image = p.Image, Width = p.Width, Height = p.Height, Words = p.Words });
            tagged.Add(tagger.Tag(p.Words, p.Width, p.Height));
        }
        var inv = Assemble.Invoice(tagged, output);
        Distribute(output, Check.Invoice(inv));
        return (inv, output);
    }, ct);

    static void Distribute(List<OcrPage> pages, List<Flag> flags)
    {
        if (pages.Count == 0) return;
        foreach (var f in flags)
        {
            if (f.LineNo == 0)
            {
                pages[0].Flags.Add(f);
                continue;
            }
            var placed = false;
            foreach (var l in pages.SelectMany(p => p.Lines).Where(l => l.Parsed.No == f.LineNo))
            {
                l.Flags.Add(f);
                placed = true;
            }
            if (!placed) pages[0].Flags.Add(f);
        }
    }
}

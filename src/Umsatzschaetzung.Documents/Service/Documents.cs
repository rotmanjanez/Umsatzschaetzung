using System.Runtime.CompilerServices;
using SkiaSharp;
using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

public sealed class Documents(IOcr? ocr, IPdfPages? pdf) : IDocuments
{
    const int PreviewDpi = 150;
    const float KeptScale = 0.5f;

    public string Reader => $"{RapidOcr.Name}|{RapidOcr.MaxImageDimension}|{Scan.Dpi}";

    public Task<List<OcrPage>> Read(string fileName, byte[] data, CancellationToken ct) =>
        Scan.Read(ocr, pdf, fileName, data, Scan.Dpi, ct);

    public async Task<List<List<OcrWord>>> Reread(byte[] data, OcrPage reading, int page, IReadOnlyList<Box> regions, CancellationToken ct)
    {
        using var image = await Scan.Page(pdf, data, page, Scan.Dpi, ct);
        var read = new List<List<OcrWord>>(regions.Count);
        foreach (var r in regions)
        {
            var words = Scan.Cut(image, reading.Correction, Rect(r)) is { } crop
                ? await ocr!.Read(crop, ct)
                : [];
            read.Add([.. words.Select(w => new OcrWord { Text = w.Text, Box = w.Box with { X = w.Box.X + r.X, Y = w.Box.Y + r.Y }, Confidence = w.Confidence })]);
        }
        return read;
    }

    // The preview keeps its own resolution; the turns do not depend on it.
    public async IAsyncEnumerable<Raster> Preview(byte[] data, IReadOnlyList<Correction> reading, [EnumeratorCancellation] CancellationToken ct)
    {
        var i = 0;
        await foreach (var image in Scan.Pages(pdf, data, PreviewDpi, ct))
            using (image)
            {
                var c = reading.ElementAtOrDefault(i++) is { } r ? new Correction { Skew = r.Skew, Turn = r.Turn, Settle = r.Settle } : new Correction();
                yield return await Task.Run(() => Scan.Upright(image, c), ct);
            }
    }

    public async IAsyncEnumerable<byte[]> Keep(byte[] data, IReadOnlyList<Correction> reading, [EnumeratorCancellation] CancellationToken ct)
    {
        if (pdf is null && InvoiceParser.Detect(data) != Kind.Image) yield break;
        var i = 0;
        await foreach (var image in Scan.Pages(pdf, data, Scan.Dpi, ct))
            using (image)
            {
                if (i >= reading.Count) yield break;
                var c = reading[i++];
                yield return await Task.Run(() => Scan.Keep(image, c, KeptScale), ct);
            }
    }

    public Raster Show(byte[] kept) => Scan.Crop(kept, new SKRectI(0, 0, int.MaxValue, int.MaxValue))!;

    public Raster? Cut(byte[] kept, Box region) =>
        Scan.Crop(kept, SKRectI.Round(new SKRect(region.X * KeptScale, region.Y * KeptScale,
            (region.X + region.W) * KeptScale, (region.Y + region.H) * KeptScale)));

    public List<Sheet> Sheets(byte[] pdf) => Richtsatz.Sheets.Read(pdf);

    public byte[] Stamp(byte[] pdf, PageMarks marks) => PdfMarks.Stamp(pdf, marks);

    static SKRectI Rect(Box b) => new(b.X, b.Y, b.X + b.W, b.Y + b.H);
}

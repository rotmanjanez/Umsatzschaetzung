using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Service;

// Everything that needs a page's pixels or a PDF's insides. Pictures come in the frame the
// reading's boxes sit in, turned and straightened as it was.
public interface IDocuments
{
    // Names what reads the pages; a kept reading is only replayed for the same reader.
    string Reader { get; }

    Task<List<OcrPage>> Read(string fileName, byte[] data, CancellationToken ct);

    // Regions of a read page, read again on their own; boxes in the frame of the reading.
    Task<List<List<OcrWord>>> Reread(byte[] data, OcrPage reading, int page, IReadOnlyList<Box> regions, CancellationToken ct);

    // The pages as the reading saw them, as many as it has.
    IAsyncEnumerable<Raster> Pages(byte[] data, IReadOnlyList<Correction> reading, CancellationToken ct);

    // Every page at a resolution for looking at, turned as the reading turned it.
    IAsyncEnumerable<Raster> Preview(byte[] data, IReadOnlyList<Correction> reading, CancellationToken ct);

    // A page kept to cut rows from: upright as the reading saw it, smaller, compressed.
    Task<byte[]> Keep(byte[] data, int page, Correction correction, CancellationToken ct);

    // A region of the reading's frame, cut from a kept page.
    Raster? Cut(byte[] kept, Box region);

    List<Sheet> Sheets(byte[] pdf);

    byte[] Stamp(byte[] pdf, PageMarks marks);
}

public interface IPdfPrinter
{
    Task<byte[]> Print(string html, CancellationToken ct);
}

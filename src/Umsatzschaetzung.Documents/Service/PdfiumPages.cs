using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace Umsatzschaetzung.Service;

public sealed class PdfiumPages : IPdfPages
{
    const double PointsPerInch = 72.0;
    const int Bgra = 4;
    const uint White = 0xFFFFFFFF;

    // Annotation appearance streams carry stamps and filled form fields, which on an
    // invoice are content, not decoration.
    const int WithAnnotations = 0x01;

    public async Task<SKBitmap> Page(byte[] pdf, int index, int dpi, CancellationToken ct)
    {
        using var document = await Task.Run(() => new Document(pdf), ct);
        if (index < 0 || index >= document.Count) throw new InvalidDataException($"pdf: keine Seite {index + 1}");
        return await Task.Run(() => document.Render(index, dpi), ct);
    }

    // One page at a time: a page is 35 MB of pixels where its PNG is under one.
    public async IAsyncEnumerable<SKBitmap> Rasterize(byte[] pdf, int dpi, [EnumeratorCancellation] CancellationToken ct)
    {
        using var document = await Task.Run(() => new Document(pdf), ct);
        for (var i = 0; i < document.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return await Task.Run(() => document.Render(i, dpi), ct);
        }
    }

    // Pdfium takes one caller at a time. The gate is held per call rather than for the life
    // of the document, so a page can be read while another document renders.
    sealed class Document : IDisposable
    {
        readonly GCHandle pinned;
        readonly nint document;

        public int Count { get; }

        public Document(byte[] pdf)
        {
            // The document reads from the buffer for as long as it is open.
            pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
            lock (Pdfium.Gate)
            {
                Pdfium.Start();
                document = Pdfium.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)pdf.Length, null);
                if (document == 0)
                {
                    pinned.Free();
                    throw new InvalidDataException("pdf: nicht lesbar");
                }
                Count = Pdfium.FPDF_GetPageCount(document);
            }
        }

        public SKBitmap Render(int index, int dpi)
        {
            lock (Pdfium.Gate)
            {
                var page = Pdfium.FPDF_LoadPage(document, index);
                if (page == 0) throw new InvalidDataException($"pdf: Seite {index + 1} nicht lesbar");
                try
                {
                    // Both the crop box and /Rotate are already in this size, and the render
                    // below applies them too, so there is no page transform left to build.
                    var width = Pdfium.FPDF_GetPageWidthF(page);
                    var height = Pdfium.FPDF_GetPageHeightF(page);
                    if (width <= 0 || height <= 0) throw new InvalidDataException("pdf: Seite ohne Abmessung");

                    var (w, h) = PdfRaster.Target(width, height, Math.Min(dpi / PointsPerInch, Scanned(page, width, height)));
                    var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
                    try
                    {
                        var target = Pdfium.FPDFBitmap_CreateEx(w, h, Bgra, bitmap.GetPixels(), bitmap.RowBytes);
                        if (target == 0) throw new InvalidOperationException("FPDFBitmap_CreateEx");
                        try
                        {
                            // A PDF page paints no background, and the recogniser wants paper.
                            Pdfium.FPDFBitmap_FillRect(target, 0, 0, w, h, White);
                            Pdfium.FPDF_RenderPageBitmap(target, page, 0, 0, w, h, 0, WithAnnotations);
                        }
                        finally
                        {
                            Pdfium.FPDFBitmap_Destroy(target);
                        }
                        bitmap.NotifyPixelsChanged();
                        return bitmap;
                    }
                    catch
                    {
                        bitmap.Dispose();
                        throw;
                    }
                }
                finally
                {
                    Pdfium.FPDF_ClosePage(page);
                }
            }
        }

        // A page that is one scanned image holds no more detail than the image's pixels; drawn
        // larger it only grows. Any other page renders at any resolution.
        static double Scanned(nint page, float width, float height)
        {
            if (Pdfium.FPDFPage_CountObjects(page) != 1) return double.MaxValue;
            var image = Pdfium.FPDFPage_GetObject(page, 0);
            return Pdfium.FPDFPageObj_GetType(image) == Pdfium.ObjImage && Pdfium.FPDFImageObj_GetImagePixelSize(image, out var w, out var h) != 0
                ? Math.Max(w, h) / (double)Math.Max(width, height)
                : double.MaxValue;
        }

        public void Dispose()
        {
            lock (Pdfium.Gate) Pdfium.FPDF_CloseDocument(document);
            pinned.Free();
        }
    }
}

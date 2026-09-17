using System.Runtime.InteropServices;
using SkiaSharp;

namespace Umsatzschätzung.Service;

public sealed class PdfiumPages : IPdfPages
{
    const double PointsPerInch = 72.0;
    const int Bgra = 4;
    const uint White = 0xFFFFFFFF;

    // Annotation appearance streams carry stamps and filled form fields, which on an
    // invoice are content, not decoration.
    const int WithAnnotations = 0x01;

    // pdfium keeps process-wide state and is not built thread-safe, so one page at a
    // time. The corpus tool runs a worker per core; rasterising is a small part of
    // what a worker does, OCR is the rest, so serialising here costs little.
    static readonly Lock Gate = new();
    static bool started;

    public Task<List<byte[]>> Render(byte[] pdf, int dpi, CancellationToken ct) =>
        Task.Run(() => RenderPages(pdf, dpi, ct), ct);

    static List<byte[]> RenderPages(byte[] pdf, int dpi, CancellationToken ct)
    {
        lock (Gate)
        {
            if (!started)
            {
                Pdfium.FPDF_InitLibrary();
                started = true;
            }

            // The document reads from the buffer for as long as it is open.
            var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
            var document = nint.Zero;
            try
            {
                document = Pdfium.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)pdf.Length, null);
                if (document == 0) throw new InvalidDataException("pdf: nicht lesbar");

                var count = Pdfium.FPDF_GetPageCount(document);
                var pages = new List<byte[]>(count);
                for (var i = 0; i < count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    pages.Add(RenderPage(document, i, dpi));
                }
                return pages;
            }
            finally
            {
                if (document != 0) Pdfium.FPDF_CloseDocument(document);
                pinned.Free();
            }
        }
    }

    static byte[] RenderPage(nint document, int index, int dpi)
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

            var (w, h) = PdfRaster.Target(width, height, dpi / PointsPerInch);
            using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Opaque));
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
            using var png = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            return png.ToArray();
        }
        finally
        {
            Pdfium.FPDF_ClosePage(page);
        }
    }
}

file static class Pdfium
{
    const string Lib = "pdfium";

    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern nint FPDF_LoadMemDocument64(nint data, nuint size, string? password);
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(nint document);
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(nint document);
    [DllImport(Lib)] public static extern nint FPDF_LoadPage(nint document, int index);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(nint page);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(nint page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(nint page);
    [DllImport(Lib)] public static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint buffer, int stride);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(nint bitmap);
    [DllImport(Lib)] public static extern void FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int x, int y, int width, int height, int rotate, int flags);
}

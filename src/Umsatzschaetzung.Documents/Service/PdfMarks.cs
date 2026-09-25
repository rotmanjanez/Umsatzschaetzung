using System.Runtime.InteropServices;
using Umsatzschaetzung.Reports;

namespace Umsatzschaetzung.Service;

// WebKit prints no @page margin boxes, so the running header and footer are stamped onto
// the printed pages, the same way on every platform.
public static class PdfMarks
{
    const float Mm = 72f / 25.4f;
    const float FontSize = 8;
    const float Leading = 11.5f;
    const float Left = 25 * Mm;
    const float Right = 20 * Mm;
    const float Baseline = 11 * Mm;
    const float Gap = 6 * Mm;
    const string Separator = "   ·   ";
    const uint Muted = 0x64;
    const uint Ink = 0x14;

    delegate int WriteBlock(nint self, nint data, nuint size);

    static readonly WriteBlock Writer = Write;
    static MemoryStream? saving;

    public static byte[] Stamp(byte[] pdf, PageMarks marks)
    {
        var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
        lock (Pdfium.Gate)
        {
            Pdfium.Start();
            var document = Pdfium.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)pdf.Length, null);
            try
            {
                if (document == 0) throw new InvalidDataException("pdf: nicht lesbar");
                var font = Pdfium.FPDFText_LoadStandardFont(document, "Times-Roman");
                try
                {
                    var count = Pdfium.FPDF_GetPageCount(document);
                    for (var i = 0; i < count; i++)
                        StampPage(document, font, i, count, marks);
                    return Save(document);
                }
                finally
                {
                    Pdfium.FPDFFont_Close(font);
                }
            }
            finally
            {
                if (document != 0) Pdfium.FPDF_CloseDocument(document);
                pinned.Free();
            }
        }
    }

    static void StampPage(nint document, nint font, int index, int count, PageMarks marks)
    {
        var page = Pdfium.FPDF_LoadPage(document, index);
        if (page == 0) throw new InvalidDataException($"pdf: Seite {index + 1} nicht lesbar");
        try
        {
            var width = Pdfium.FPDF_GetPageWidthF(page);
            var height = Pdfium.FPDF_GetPageHeightF(page);
            if (index > 0)
            {
                Place(page, Text(document, font, marks.TopLeft, Muted), Left, height - Baseline);
                PlaceRight(page, Text(document, font, marks.TopRight, Muted), width - Right, height - Baseline);
            }
            var number = Text(document, font, $"Seite {index + 1} von {count}", Ink);
            var room = width - Left - Right - Gap - Width(number);
            PlaceRight(page, number, width - Right, Baseline);
            var lines = Lines(document, font, marks.Footer, room);
            for (var i = 0; i < lines.Count; i++)
                Place(page, lines[i], Left, Baseline + (lines.Count - 1 - i) * Leading);
            Pdfium.FPDFPage_GenerateContent(page);
        }
        finally
        {
            Pdfium.FPDF_ClosePage(page);
        }
    }

    // The footer breaks between its fields, never inside one.
    static List<nint> Lines(nint document, nint font, IReadOnlyList<string> fields, float room)
    {
        List<nint> lines = [];
        var line = "";
        nint fitting = 0;
        foreach (var field in fields)
        {
            if (line.Length == 0)
            {
                line = field;
                fitting = Text(document, font, line, Ink);
                continue;
            }
            var longer = Text(document, font, line + Separator + field, Ink);
            if (Width(longer) <= room)
            {
                Pdfium.FPDFPageObj_Destroy(fitting);
                (line, fitting) = (line + Separator + field, longer);
                continue;
            }
            Pdfium.FPDFPageObj_Destroy(longer);
            lines.Add(fitting);
            line = field;
            fitting = Text(document, font, line, Ink);
        }
        if (fitting != 0) lines.Add(fitting);
        return lines;
    }

    static nint Text(nint document, nint font, string text, uint gray)
    {
        var obj = Pdfium.FPDFPageObj_CreateTextObj(document, font, FontSize);
        Pdfium.FPDFText_SetText(obj, text);
        Pdfium.FPDFPageObj_SetFillColor(obj, gray, gray, gray, 255);
        return obj;
    }

    static float Width(nint obj) =>
        Pdfium.FPDFPageObj_GetBounds(obj, out var left, out _, out var right, out _) != 0 ? right - left : 0;

    static void Place(nint page, nint obj, float x, float y)
    {
        Pdfium.FPDFPageObj_Transform(obj, 1, 0, 0, 1, x, y);
        Pdfium.FPDFPage_InsertObject(page, obj);
    }

    static void PlaceRight(nint page, nint obj, float right, float y) => Place(page, obj, right - Width(obj), y);

    static byte[] Save(nint document)
    {
        using var output = saving = new MemoryStream();
        var writer = new FileWrite { Version = 1, WriteBlock = Marshal.GetFunctionPointerForDelegate(Writer) };
        try
        {
            if (Pdfium.FPDF_SaveAsCopy(document, ref writer, 0) == 0) throw new InvalidOperationException("pdf: nicht gespeichert");
            return output.ToArray();
        }
        finally
        {
            saving = null;
        }
    }

    static int Write(nint self, nint data, nuint size)
    {
        var chunk = new byte[size];
        Marshal.Copy(data, chunk, 0, chunk.Length);
        saving!.Write(chunk);
        return 1;
    }
}

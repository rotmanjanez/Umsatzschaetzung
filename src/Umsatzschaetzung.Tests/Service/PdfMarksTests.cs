using System.Runtime.InteropServices;
using System.Text;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public class PdfMarksTests
{
    static readonly byte[] TwoPages = File.ReadAllBytes(TestData.Fixture("dataset/2025/baeckerei/2025-01-10_SR-250107.pdf.scan.pdf"));
    static readonly PageMarks Marks = new("Umsatzschätzung · Bäckerei", "01.01.2025 bis 31.12.2025", ["Steuernummer 12/345", "Datum 24.09.2026"]);

    static List<string> Texts(byte[] pdf)
    {
        var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
        lock (Pdfium.Gate)
        {
            Pdfium.Start();
            var document = Pdfium.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)pdf.Length, null);
            try
            {
                return [.. Enumerable.Range(0, Pdfium.FPDF_GetPageCount(document)).Select(i => Text(document, i))];
            }
            finally
            {
                Pdfium.FPDF_CloseDocument(document);
                pinned.Free();
            }
        }
    }

    static string Text(nint document, int index)
    {
        var page = Pdfium.FPDF_LoadPage(document, index);
        var text = Pdfium.FPDFText_LoadPage(page);
        var b = new StringBuilder();
        for (var i = 0; i < Pdfium.FPDFText_CountChars(text); i++)
            b.Append((char)Pdfium.FPDFText_GetUnicode(text, i));
        Pdfium.FPDFText_ClosePage(text);
        Pdfium.FPDF_ClosePage(page);
        return b.ToString();
    }

    [Fact]
    public void EveryPageCarriesTheFooterAndItsNumber()
    {
        var pages = Texts(PdfMarks.Stamp(TwoPages, Marks));

        Assert.Equal(2, pages.Count);
        Assert.All(pages, p => Assert.Contains("Steuernummer 12/345", p));
        Assert.All(pages, p => Assert.Contains("Datum 24.09.2026", p));
        Assert.Contains("Seite 1 von 2", pages[0]);
        Assert.Contains("Seite 2 von 2", pages[1]);
    }

    [Fact]
    public void TheHeaderStartsOnTheSecondPage()
    {
        var pages = Texts(PdfMarks.Stamp(TwoPages, Marks));

        Assert.DoesNotContain("Bäckerei", pages[0]);
        Assert.Contains("Umsatzschätzung · Bäckerei", pages[1]);
        Assert.Contains("01.01.2025 bis 31.12.2025", pages[1]);
    }
}

using System.Runtime.InteropServices;
using System.Text;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Richtsatz;

// Page geometry with y growing downwards, so that reading order is ascending.
readonly record struct Word(string Text, double X0, double X1, double Top, double Bottom, double Baseline, double Size)
{
    public double Xc => (X0 + X1) / 2;
}

readonly record struct Rule(double X0, double X1, double Top, double Bottom)
{
    public double Width => X1 - X0;
    public double Height => Bottom - Top;
}

sealed record Sheet(int Number, double Width, double Height, List<Word> Words, List<Rule> Rules);

static class Sheets
{
    public static List<Sheet> Read(byte[] pdf)
    {
        lock (Pdfium.Gate)
        {
            Pdfium.Start();
            var pinned = GCHandle.Alloc(pdf, GCHandleType.Pinned);
            var document = nint.Zero;
            try
            {
                document = Pdfium.FPDF_LoadMemDocument64(pinned.AddrOfPinnedObject(), (nuint)pdf.Length, null);
                if (document == 0) throw new InvalidDataException("pdf: nicht lesbar");

                var count = Pdfium.FPDF_GetPageCount(document);
                var sheets = new List<Sheet>(count);
                for (var i = 0; i < count; i++) sheets.Add(ReadPage(document, i));
                return sheets;
            }
            finally
            {
                if (document != 0) Pdfium.FPDF_CloseDocument(document);
                pinned.Free();
            }
        }
    }

    static Sheet ReadPage(nint document, int index)
    {
        var page = Pdfium.FPDF_LoadPage(document, index);
        if (page == 0) throw new InvalidDataException($"pdf: Seite {index + 1} nicht lesbar");
        try
        {
            // Text and objects come back in the coordinates of the media box. The booklet
            // years impose two printed pages on one of those and show each through a crop
            // box of its own, so everything outside it belongs to the neighbouring page.
            if (Pdfium.FPDF_GetPageBoundingBox(page, out var box) == 0) throw new InvalidDataException($"pdf: Seite {index + 1} ohne Abmessung");
            var width = box.Right - box.Left;
            var height = box.Top - box.Bottom;
            var rules = new List<Rule>();
            ReadRules(Pdfium.FPDFPage_CountObjects(page), i => Pdfium.FPDFPage_GetObject(page, i), box, rules);
            return new Sheet(index + 1, width, height,
                [.. ReadWords(page, box).Where(w => Inside(w.Xc, w.Baseline, width, height))],
                [.. rules.Where(r => Inside((r.X0 + r.X1) / 2, (r.Top + r.Bottom) / 2, width, height))]);
        }
        finally
        {
            Pdfium.FPDF_ClosePage(page);
        }
    }

    static bool Inside(double x, double y, double width, double height) =>
        x >= 0 && x <= width && y >= 0 && y <= height;

    static List<Word> ReadWords(nint page, Rect box)
    {
        var words = new List<Word>();
        var text = Pdfium.FPDFText_LoadPage(page);
        if (text == 0) return words;
        try
        {
            var buf = new StringBuilder();
            double x0 = 0, x1 = 0, top = 0, bottom = 0, baseline = 0, advance = 0, size = 0;

            void Flush()
            {
                if (buf.Length > 0) words.Add(new Word(buf.ToString(), x0, x1, top, bottom, baseline, size));
                buf.Clear();
            }

            var count = Pdfium.FPDFText_CountChars(text);
            for (var i = 0; i < count; i++)
            {
                var u = Pdfium.FPDFText_GetUnicode(text, i);
                if (u is 0 or > 0x10FFFF || char.IsWhiteSpace((char)u) || char.IsControl((char)u))
                {
                    Flush();
                    continue;
                }

                Pdfium.FPDFText_GetCharBox(text, i, out var left, out var right, out var low, out var high);
                Pdfium.FPDFText_GetCharOrigin(text, i, out _, out var originY);
                var cTop = box.Top - high;
                var cBottom = box.Top - low;
                var cBase = box.Top - originY;
                left -= box.Left;
                right -= box.Left;

                // The gap that parts two words is the one between their advances: the ink of
                // a narrow glyph such as 1 sits far inside its own, and a break there would
                // split a number in two.
                Pdfium.FPDFText_GetLooseCharBox(text, i, out var loose);
                if (buf.Length > 0 && (Math.Abs(cBase - baseline) > 1.5 || loose.Left - advance > 1.2)) Flush();

                if (buf.Length == 0) (x0, top, bottom, baseline, size) = (left, cTop, cBottom, cBase, Pdfium.FPDFText_GetFontSize(text, i));
                else (top, bottom) = (Math.Min(top, cTop), Math.Max(bottom, cBottom));
                x1 = right;
                advance = loose.Right;
                buf.Append(char.ConvertFromUtf32((int)u));
            }
            Flush();
            return words;
        }
        finally
        {
            Pdfium.FPDFText_ClosePage(text);
        }
    }

    // The table borders are thin filled rectangles, split into a run of segments per
    // line, so callers cluster them rather than reading one object per rule.
    static void ReadRules(int count, Func<int, nint> at, Rect box, List<Rule> rules)
    {
        for (var i = 0; i < count; i++)
        {
            var obj = at(i);
            if (obj == 0) continue;
            var type = Pdfium.FPDFPageObj_GetType(obj);
            if (type == Pdfium.ObjForm)
            {
                ReadRules(Pdfium.FPDFFormObj_CountObjects(obj), j => Pdfium.FPDFFormObj_GetObject(obj, (uint)j), box, rules);
                continue;
            }
            if (type != Pdfium.ObjPath) continue;
            if (Pdfium.FPDFPageObj_GetBounds(obj, out var left, out var low, out var right, out var high) == 0) continue;
            rules.Add(new Rule(left - box.Left, right - box.Left, box.Top - high, box.Top - low));
        }
    }
}

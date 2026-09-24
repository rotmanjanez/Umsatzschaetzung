using System.Runtime.InteropServices;

namespace Umsatzschaetzung.Service;

// pdfium keeps process-wide state and is not built thread-safe, so everything that
// touches it holds Gate and starts the library through Start.
[StructLayout(LayoutKind.Sequential)]
internal struct Rect
{
    public float Left, Top, Right, Bottom;
}

internal static class Pdfium
{
    const string Lib = "pdfium";

    public static readonly Lock Gate = new();
    static bool started;

    public static void Start()
    {
        if (started) return;
        FPDF_InitLibrary();
        started = true;
    }

    public const int ObjPath = 2;
    public const int ObjForm = 5;

    [DllImport(Lib)] public static extern void FPDF_InitLibrary();
    [DllImport(Lib)] public static extern nint FPDF_LoadMemDocument64(nint data, nuint size, string? password);
    [DllImport(Lib)] public static extern void FPDF_CloseDocument(nint document);
    [DllImport(Lib)] public static extern int FPDF_GetPageCount(nint document);
    [DllImport(Lib)] public static extern nint FPDF_LoadPage(nint document, int index);
    [DllImport(Lib)] public static extern void FPDF_ClosePage(nint page);
    [DllImport(Lib)] public static extern int FPDF_GetPageBoundingBox(nint page, out Rect rect);
    [DllImport(Lib)] public static extern float FPDF_GetPageWidthF(nint page);
    [DllImport(Lib)] public static extern float FPDF_GetPageHeightF(nint page);

    [DllImport(Lib)] public static extern nint FPDFBitmap_CreateEx(int width, int height, int format, nint buffer, int stride);
    [DllImport(Lib)] public static extern void FPDFBitmap_Destroy(nint bitmap);
    [DllImport(Lib)] public static extern void FPDFBitmap_FillRect(nint bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Lib)] public static extern void FPDF_RenderPageBitmap(nint bitmap, nint page, int x, int y, int width, int height, int rotate, int flags);

    [DllImport(Lib)] public static extern nint FPDFText_LoadPage(nint page);
    [DllImport(Lib)] public static extern void FPDFText_ClosePage(nint textPage);
    [DllImport(Lib)] public static extern int FPDFText_CountChars(nint textPage);
    [DllImport(Lib)] public static extern uint FPDFText_GetUnicode(nint textPage, int index);
    [DllImport(Lib)] public static extern double FPDFText_GetFontSize(nint textPage, int index);
    [DllImport(Lib)] public static extern int FPDFText_GetLooseCharBox(nint textPage, int index, out Rect rect);
    [DllImport(Lib)] public static extern int FPDFText_GetCharOrigin(nint textPage, int index, out double x, out double y);
    [DllImport(Lib)] public static extern int FPDFText_GetCharBox(nint textPage, int index, out double left, out double right, out double bottom, out double top);

    [DllImport(Lib)] public static extern int FPDFPage_CountObjects(nint page);
    [DllImport(Lib)] public static extern nint FPDFPage_GetObject(nint page, int index);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetType(nint obj);
    [DllImport(Lib)] public static extern int FPDFPageObj_GetBounds(nint obj, out float left, out float bottom, out float right, out float top);
    [DllImport(Lib)] public static extern int FPDFFormObj_CountObjects(nint obj);
    [DllImport(Lib)] public static extern nint FPDFFormObj_GetObject(nint obj, uint index);

    [DllImport(Lib)] public static extern nint FPDFText_LoadStandardFont(nint document, string font);
    [DllImport(Lib)] public static extern void FPDFFont_Close(nint font);
    [DllImport(Lib)] public static extern nint FPDFPageObj_CreateTextObj(nint document, nint font, float size);
    [DllImport(Lib)] public static extern int FPDFText_SetText(nint textObject, [MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport(Lib)] public static extern int FPDFPageObj_SetFillColor(nint obj, uint r, uint g, uint b, uint a);
    [DllImport(Lib)] public static extern void FPDFPageObj_Transform(nint obj, double a, double b, double c, double d, double e, double f);
    [DllImport(Lib)] public static extern void FPDFPageObj_Destroy(nint obj);
    [DllImport(Lib)] public static extern void FPDFPage_InsertObject(nint page, nint obj);
    [DllImport(Lib)] public static extern int FPDFPage_GenerateContent(nint page);
    [DllImport(Lib)] public static extern int FPDF_SaveAsCopy(nint document, ref FileWrite writer, uint flags);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FileWrite
{
    public int Version;
    public nint WriteBlock;
}

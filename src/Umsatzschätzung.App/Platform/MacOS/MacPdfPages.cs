using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Platform;

[SupportedOSPlatform("macos")]
public sealed class MacPdfPages : IPdfPages
{
    const int CropBox = 1;
    const int MediaBox = 0;
    const uint NoneSkipLast = 5;
    const uint Utf8 = 0x08000100;
    const double PointsPerInch = 72.0;

    public Task<List<byte[]>> Render(byte[] pdf, int dpi, CancellationToken ct) =>
        Task.Run(() => RenderPages(pdf, dpi, ct), ct);

    static List<byte[]> RenderPages(byte[] pdf, int dpi, CancellationToken ct)
    {
        var data = CF.CFDataCreate(0, pdf, pdf.Length);
        if (data == 0) throw new InvalidOperationException("CFDataCreate");
        nint provider = 0;
        nint document = 0;
        try
        {
            provider = CG.CGDataProviderCreateWithCFData(data);
            if (provider == 0) throw new InvalidOperationException("CGDataProviderCreateWithCFData");
            document = CG.CGPDFDocumentCreateWithProvider(provider);
            if (document == 0) throw new InvalidDataException("pdf: nicht lesbar");

            var count = (int)CG.CGPDFDocumentGetNumberOfPages(document);
            var pages = new List<byte[]>(count);
            for (var i = 1; i <= count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var page = CG.CGPDFDocumentGetPage(document, i);
                if (page == 0) throw new InvalidDataException($"pdf: Seite {i} nicht lesbar");
                pages.Add(RenderPage(page, dpi));
            }
            return pages;
        }
        finally
        {
            if (document != 0) CG.CGPDFDocumentRelease(document);
            if (provider != 0) CG.CGDataProviderRelease(provider);
            CF.CFRelease(data);
        }
    }

    static byte[] RenderPage(nint page, int dpi)
    {
        var box = CG.CGPDFPageGetBoxRect(page, CropBox);
        if (box.Width <= 0 || box.Height <= 0) box = CG.CGPDFPageGetBoxRect(page, MediaBox);
        if (box.Width <= 0 || box.Height <= 0) throw new InvalidDataException("pdf: Seite ohne Abmessung");

        // Both boxes are stated before /Rotate is applied, so a landscape page stored
        // upright reports portrait dimensions and has to be measured turned.
        var quarters = Quarters(page);
        var turned = quarters % 2 != 0;
        var width = turned ? box.Height : box.Width;
        var height = turned ? box.Width : box.Height;
        var (w, h) = PdfRaster.Target(width, height, dpi / PointsPerInch);

        var space = CG.CGColorSpaceCreateDeviceRGB();
        nint context = 0;
        nint image = 0;
        try
        {
            context = CG.CGBitmapContextCreate(0, w, h, 8, 0, space, NoneSkipLast);
            if (context == 0) throw new InvalidOperationException("CGBitmapContextCreate");

            // A PDF page paints no background, and the recogniser wants paper, not black.
            var target = new CGRect(0, 0, w, h);
            CG.CGContextSetRGBFillColor(context, 1, 1, 1, 1);
            CG.CGContextFillRect(context, target);
            CG.CGContextClipToRect(context, target);

            // CGPDFPageGetDrawingTransform only ever shrinks a page to fit, so above
            // 72 dpi it centres the page at its original size instead of filling the
            // bitmap. Scale, turn and shift the page into place by hand instead.
            CG.CGContextScaleCTM(context, w / width, h / height);
            Turn(context, quarters, width, height);
            CG.CGContextTranslateCTM(context, -box.X, -box.Y);
            CG.CGContextDrawPDFPage(context, page);

            image = CG.CGBitmapContextCreateImage(context);
            if (image == 0) throw new InvalidOperationException("CGBitmapContextCreateImage");
            return Png(image);
        }
        finally
        {
            if (image != 0) CG.CGImageRelease(image);
            if (context != 0) CG.CGContextRelease(context);
            CG.CGColorSpaceRelease(space);
        }
    }

    // /Rotate turns the page clockwise for display, which is the opposite sense to
    // CGContextRotateCTM, and every quarter turn but the half one also moves the page
    // off the context's bottom-left origin.
    static void Turn(nint context, int quarters, double width, double height)
    {
        switch (quarters)
        {
            case 1:
                CG.CGContextTranslateCTM(context, 0, height);
                CG.CGContextRotateCTM(context, -Math.PI / 2);
                break;
            case 2:
                CG.CGContextTranslateCTM(context, width, height);
                CG.CGContextRotateCTM(context, Math.PI);
                break;
            case 3:
                CG.CGContextTranslateCTM(context, width, 0);
                CG.CGContextRotateCTM(context, Math.PI / 2);
                break;
        }
    }

    // /Rotate is a multiple of 90 by specification, but nothing enforces it and a
    // negative angle is common enough.
    static int Quarters(nint page)
    {
        var degrees = CG.CGPDFPageGetRotationAngle(page) % 360;
        if (degrees < 0) degrees += 360;
        return (int)Math.Round(degrees / 90.0) % 4;
    }

    static byte[] Png(nint image)
    {
        var buffer = CF.CFDataCreateMutable(0, 0);
        var type = CF.CFStringCreateWithCString(0, "public.png", Utf8);
        nint destination = 0;
        try
        {
            destination = ImageIO.CGImageDestinationCreateWithData(buffer, type, 1, 0);
            if (destination == 0) throw new InvalidOperationException("CGImageDestinationCreateWithData");
            ImageIO.CGImageDestinationAddImage(destination, image, 0);
            if (!ImageIO.CGImageDestinationFinalize(destination)) throw new InvalidOperationException("CGImageDestinationFinalize");
            var bytes = new byte[(int)CF.CFDataGetLength(buffer)];
            Marshal.Copy(CF.CFDataGetBytePtr(buffer), bytes, 0, bytes.Length);
            return bytes;
        }
        finally
        {
            if (destination != 0) CF.CFRelease(destination);
            CF.CFRelease(type);
            CF.CFRelease(buffer);
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
file struct CGRect(double x, double y, double width, double height)
{
    public double X = x, Y = y, Width = width, Height = height;
}

[SupportedOSPlatform("macos")]
file static class CF
{
    const string Lib = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    [DllImport(Lib)] public static extern nint CFDataCreate(nint allocator, byte[] bytes, nint length);
    [DllImport(Lib)] public static extern nint CFDataCreateMutable(nint allocator, nint capacity);
    [DllImport(Lib)] public static extern nint CFDataGetLength(nint data);
    [DllImport(Lib)] public static extern nint CFDataGetBytePtr(nint data);
    [DllImport(Lib)] public static extern nint CFStringCreateWithCString(nint allocator, string cstr, uint encoding);
    [DllImport(Lib)] public static extern void CFRelease(nint cf);
}

[SupportedOSPlatform("macos")]
file static class CG
{
    const string Lib = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(Lib)] public static extern nint CGDataProviderCreateWithCFData(nint data);
    [DllImport(Lib)] public static extern void CGDataProviderRelease(nint provider);
    [DllImport(Lib)] public static extern nint CGPDFDocumentCreateWithProvider(nint provider);
    [DllImport(Lib)] public static extern void CGPDFDocumentRelease(nint document);
    [DllImport(Lib)] public static extern nint CGPDFDocumentGetNumberOfPages(nint document);
    [DllImport(Lib)] public static extern nint CGPDFDocumentGetPage(nint document, nint page);
    [DllImport(Lib)] public static extern CGRect CGPDFPageGetBoxRect(nint page, int box);
    [DllImport(Lib)] public static extern int CGPDFPageGetRotationAngle(nint page);
    [DllImport(Lib)] public static extern nint CGColorSpaceCreateDeviceRGB();
    [DllImport(Lib)] public static extern void CGColorSpaceRelease(nint space);
    [DllImport(Lib)] public static extern nint CGBitmapContextCreate(nint data, nint width, nint height, nint bitsPerComponent, nint bytesPerRow, nint space, uint bitmapInfo);
    [DllImport(Lib)] public static extern nint CGBitmapContextCreateImage(nint context);
    [DllImport(Lib)] public static extern void CGContextRelease(nint context);
    [DllImport(Lib)] public static extern void CGContextSetRGBFillColor(nint context, double red, double green, double blue, double alpha);
    [DllImport(Lib)] public static extern void CGContextFillRect(nint context, CGRect rect);
    [DllImport(Lib)] public static extern void CGContextClipToRect(nint context, CGRect rect);
    [DllImport(Lib)] public static extern void CGContextScaleCTM(nint context, double sx, double sy);
    [DllImport(Lib)] public static extern void CGContextTranslateCTM(nint context, double tx, double ty);
    [DllImport(Lib)] public static extern void CGContextRotateCTM(nint context, double angle);
    [DllImport(Lib)] public static extern void CGContextDrawPDFPage(nint context, nint page);
    [DllImport(Lib)] public static extern void CGImageRelease(nint image);
}

[SupportedOSPlatform("macos")]
file static class ImageIO
{
    const string Lib = "/System/Library/Frameworks/ImageIO.framework/ImageIO";

    [DllImport(Lib)] public static extern nint CGImageDestinationCreateWithData(nint data, nint type, nint count, nint options);
    [DllImport(Lib)] public static extern void CGImageDestinationAddImage(nint destination, nint image, nint properties);
    [DllImport(Lib)] [return: MarshalAs(UnmanagedType.U1)] public static extern bool CGImageDestinationFinalize(nint destination);
}

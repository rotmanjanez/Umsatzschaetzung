using RapidOcrNet;
using SkiaSharp;
using Umsatzschaetzung.Model;
using Engine = RapidOcrNet.RapidOcr;

namespace Umsatzschaetzung.Service;

// The v6 detector with the v5 latin recogniser: v6 reads scripts these invoices never
// carry and is slower for it. The two normalise differently, so the detector's mean and
// deviation travel with its path. Threads is per instance: one page at a time wants every
// core, a corpus run with a page per core wants one each.
public sealed class RapidOcr(int threads = 0) : IOcr, IDisposable
{
    public const string Name = "RapidOcrNet/PP-OCRv6-det-small+PP-OCRv5-latin-rec";

    // The detector's long-side cap; boxes come back in source pixels regardless.
    public const int MaxImageDimension = 4000;

    static readonly RapidOcrOptions Options =
        RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true, MaxSideLen = MaxImageDimension };

    static readonly RapidOcrModelSet Models = RapidOcrModelSet.PPOCRv5Latin with
    {
        DetModelPath = AppFiles.Beside("models/v6/PP-OCRv6_det_small.onnx"),
        DetMean = RapidOcrModelSet.PPOCRv6Small.DetMean,
        DetStd = RapidOcrModelSet.PPOCRv6Small.DetStd,
        ClsModelPath = AppFiles.Beside(RapidOcrModelSet.PPOCRv5Latin.ClsModelPath),
        RecModelPath = AppFiles.Beside(RapidOcrModelSet.PPOCRv5Latin.RecModelPath),
        KeysPath = AppFiles.Beside(RapidOcrModelSet.PPOCRv5Latin.KeysPath),
    };

    Engine? engine;

    public void Dispose() => engine?.Dispose();

    public Task<OcrPage> Recognize(byte[] image, CancellationToken ct) => Task.Run(async () =>
    {
        using var decoded = Decode(image);
        return await Recognize(decoded, image);
    }, ct);

    public Task<OcrPage> Recognize(SKBitmap page, CancellationToken ct) => Task.Run(() => Recognize(page, null), ct);

    // Show-through goes first, on the original pixels, so it cannot vote on the lean; then
    // the page is straightened and read. A sideways or upside-down page is turned and read
    // again, since the boxes of the first pass sit in the turned frame. The record of the
    // page is encoded beside the read: the read wants every core, the encoder one of them.
    async Task<OcrPage> Recognize(SKBitmap decoded, byte[]? delivered)
    {
        using var cleaned = Deink.Apply(decoded);
        using var straightened = Deskew.Apply(cleaned ?? decoded);
        var page = straightened ?? cleaned ?? decoded;
        var image = delivered is not null && ReferenceEquals(page, decoded) ? Task.FromResult(delivered) : Task.Run(() => Encode(page));
        var first = Read(page);
        var turn = Correction(first);
        if (turn == 0) return Page(page, first, await image);
        await image;
        using var turned = Rotate(page, turn);
        using var settled = Deskew.Apply(turned);
        var upright = settled ?? turned;
        var record = Task.Run(() => Encode(upright));
        return Page(upright, Read(upright), await record);
    }

    static OcrPage Page(SKBitmap page, OcrResult result, byte[] image) =>
        new() { Width = page.Width, Height = page.Height, Words = Words(result), Image = image };

    OcrResult Read(SKBitmap page)
    {
        engine ??= Open();
        return engine.Detect(page, Options);
    }

    static List<OcrWord> Words(OcrResult result)
    {
        var words = new List<OcrWord>();
        foreach (var block in result.TextBlocks)
            foreach (var word in block.WordResults ?? [])
                if (!string.IsNullOrWhiteSpace(word.Text))
                    words.Add(new OcrWord { Text = word.Text, Box = Bounds(word.BoxPoints), Confidence = word.Score });
        return words;
    }

    // Lines running down the page mean a quarter turn; the direction classifier having
    // flipped every crop means upside down. Strict majorities only: turning an upright
    // page costs far more than leaving a sideways one.
    static int Correction(OcrResult result)
    {
        int tall = 0, wide = 0, flipped = 0, upright = 0;
        foreach (var block in result.TextBlocks)
        {
            var box = Bounds(block.BoxPoints);
            if (box.H > box.W) tall++; else wide++;
            if (block.AngleIndex == 1) flipped++; else upright++;
        }
        return (tall > wide, flipped > upright) switch
        {
            (true, true) => 270,
            (true, false) => 90,
            (false, true) => 180,
            _ => 0,
        };
    }

    Engine Open()
    {
        var engine = new Engine();
        try { if (threads > 0) engine.InitModels(Models, threads); else engine.InitModels(Models); }
        catch (Exception e)
        {
            engine.Dispose();
            throw new InvalidOperationException(
                "Die Texterkennungsmodelle konnten nicht geladen werden. Erwartet unter " +
                AppFiles.Beside("models") + ".", e);
        }
        return engine;
    }

    static Box Bounds(SKPointI[] quad)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = int.MinValue, y1 = int.MinValue;
        foreach (var p in quad)
        {
            if (p.X < x0) x0 = p.X;
            if (p.Y < y0) y0 = p.Y;
            if (p.X > x1) x1 = p.X;
            if (p.Y > y1) y1 = p.Y;
        }
        return new Box(x0, y0, x1 - x0, y1 - y0);
    }

    // The detector reads straight off the pixel buffer and accepts only this layout.
    static SKBitmap Decode(byte[] image)
    {
        var decoded = SKBitmap.Decode(image)
            ?? throw new InvalidOperationException("Das Seitenbild konnte nicht gelesen werden.");
        if (decoded.ColorType == SKColorType.Bgra8888) return decoded;
        using (decoded)
            return decoded.Copy(SKColorType.Bgra8888)
                ?? throw new InvalidOperationException("Das Seitenbild konnte nicht umgewandelt werden.");
    }

    static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        var swap = degrees != 180;
        var width = swap ? source.Height : source.Width;
        var height = swap ? source.Width : source.Height;
        var turned = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(turned);
        canvas.Translate(degrees == 90 ? width : degrees == 180 ? width : 0,
                         degrees == 180 ? height : degrees == 270 ? height : 0);
        canvas.RotateDegrees(degrees);
        canvas.DrawBitmap(source, 0, 0);
        return turned;
    }

    static byte[] Encode(SKBitmap bitmap) => PdfiumPages.Png(bitmap);
}

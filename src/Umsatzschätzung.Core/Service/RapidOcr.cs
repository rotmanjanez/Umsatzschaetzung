using RapidOcrNet;
using SkiaSharp;
using Umsatzschätzung.Model;
using Engine = RapidOcrNet.RapidOcr;

namespace Umsatzschätzung.Service;

public sealed class RapidOcr : IOcr, IDisposable
{
    public const string Name = "RapidOcrNet/PP-OCRv6-det-small+PP-OCRv5-latin-rec";

    // The detector caps the long side at this before it runs. Boxes come back in
    // source pixels regardless, but the cap is what limits how small a glyph may be
    // on the page and still be read, so the corpus dumps record it.
    public const int MaxImageDimension = 4000;

    static readonly RapidOcrOptions Options =
        RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true, MaxSideLen = MaxImageDimension };

    // The v6 detector is paired with the v5 latin recogniser: v6 recognises far more
    // scripts than these invoices need and is slower for it. The two halves normalise
    // differently — v6 wants [-1, 1], v5 wants ImageNet statistics — so the detector's
    // own mean and deviation have to travel with its path.
    //
    // The model paths in the package presets are relative, and the corpus tool is run
    // from wherever the corpus lives.
    static readonly RapidOcrModelSet Models = RapidOcrModelSet.PPOCRv5Latin with
    {
        DetModelPath = Beside("models/v6/PP-OCRv6_det_small.onnx"),
        DetMean = RapidOcrModelSet.PPOCRv6Small.DetMean,
        DetStd = RapidOcrModelSet.PPOCRv6Small.DetStd,
        ClsModelPath = Beside(RapidOcrModelSet.PPOCRv5Latin.ClsModelPath),
        RecModelPath = Beside(RapidOcrModelSet.PPOCRv5Latin.RecModelPath),
        KeysPath = Beside(RapidOcrModelSet.PPOCRv5Latin.KeysPath),
    };

    static string Beside(string path) => AppFiles.Beside(path);

    readonly RapidOcrModelSet models;
    readonly RapidOcrOptions options;
    readonly int threads;
    Engine? engine;

    // Threads is per instance, not per machine: one page at a time wants every core,
    // but a corpus run with a page per core wants one core each.
    public RapidOcr(int threads = 0) : this(Models, Options, threads) { }

    internal RapidOcr(RapidOcrModelSet models, RapidOcrOptions options, int threads = 0)
    {
        this.models = models;
        this.options = options;
        this.threads = threads;
    }

    public void Dispose() => engine?.Dispose();

    public Task<OcrPageWords> Recognize(byte[] image, CancellationToken ct) => Task.Run(() =>
    {
        using var decoded = Decode(image);
        var first = Read(decoded);
        var turn = Correction(first);
        if (turn == 0) return new OcrPageWords(decoded.Width, decoded.Height, Words(first));
        // The boxes from the first pass sit in the rotated frame, and reading the page
        // the right way up is a little better than reading it sideways, so the corrected
        // page is read again rather than having its boxes turned.
        using var turned = Rotate(decoded, turn);
        return new OcrPageWords(turned.Width, turned.Height, Words(Read(turned)), Encode(turned));
    }, ct);

    OcrResult Read(SKBitmap page)
    {
        engine ??= Open();
        return engine.Detect(page, options);
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

    // Degrees to turn the page by to stand it up. The recogniser reads a sideways or
    // upside-down page perfectly well, so the text says nothing about which way up it
    // is — but two things that fall out of the same pass do. Lines running down the
    // page instead of across it mean a quarter turn is needed, and the direction
    // classifier having flipped every crop means the page is upside down. Together
    // they separate all four orientations; on a real page both votes are unanimous.
    static int Correction(OcrResult result)
    {
        int tall = 0, wide = 0, flipped = 0, upright = 0;
        foreach (var block in result.TextBlocks)
        {
            var box = Bounds(block.BoxPoints);
            if (box.H > box.W) tall++; else wide++;
            if (block.AngleIndex == 1) flipped++; else upright++;
        }
        // Strict majorities only. A blank page, or one the detector found too little on
        // to be sure about, is left alone: turning an upright page costs far more than
        // leaving a sideways one.
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
        try { if (threads > 0) engine.InitModels(models, threads); else engine.InitModels(models); }
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

    // The detector normalises straight off the pixel buffer and accepts only these two
    // layouts, so everything below stays in one of them.
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

    static byte[] Encode(SKBitmap bitmap)
    {
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}

using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.EP.WebGpu;
using RapidOcrNet;
using SkiaSharp;
using Umsatzschaetzung.Model;
using Engine = RapidOcrNet.RapidOcr;

namespace Umsatzschaetzung.Service;

// The v6 detector with the v5 latin recogniser: v6 reads scripts these invoices never
// carry and is slower for it. The two normalise differently, so the detector's mean and
// deviation travel with its path. Threads is the detector's on the CPU, per instance: one page
// at a time wants every core, a corpus run with a page per core wants one each. The crops
// are read one per core, each on a single thread.
public sealed class RapidOcr(int threads = 0) : IOcr, IDisposable
{
    public const string Name = "RapidOcrNet/PP-OCRv6-det-small+PP-OCRv5-latin-rec";

    // The detector's long-side cap; boxes come back in source pixels regardless.
    public const int MaxImageDimension = 4000;

    // The detector finds lines as well at a third of an A4 page's 300 dpi pixels and takes a
    // third of the time; the lines are still cut from the full page.
    const int DetectorPixels = 2_600_000;

    // Nothing below the page is ever turned. The direction classifier votes and turns nothing
    // itself: on a line of two glyphs it is as confident as on a sentence and wrong often enough
    // that a "kg" comes back upside down, reads as "ER" and is dropped for scoring below
    // TextScore. A crop taller than it is wide holds no vertical script either - it is a
    // position number in a narrow column - and a quarter turn loses it the same way. Turn
    // reads the classifier's votes and turns the whole page, which is the only turn a German
    // invoice needs. What is left of a tall box is a stack: a unit column printed tightly
    // enough that the detector joined three "kg" into one box, which no line reader can read.
    // It is cut back into lines before it is read. The vote is left to the longest lines, which
    // the classifier gets right; the short ones only cost it time.
    static readonly RapidOcrOptions Options = RapidOcrOptions.PPOCRv6 with
    {
        ReturnWordBox = true,
        MaxSideLen = MaxImageDimension,
        DetMaxPixels = DetectorPixels,
        ClsRotate = false,
        RotateTallCrops = false,
        SplitStackedCrops = true,
        ClsMaxCrops = 32,
    };

    public static string Detector => Accelerator.Available ? "WebGPU" : "CPU";

    static readonly RapidOcrModelSet Models = RapidOcrModelSet.PPOCRv5Latin with
    {
        DetModelPath = AppFiles.Beside("models/v6/PP-OCRv6_det_small.onnx"),
        DetMean = RapidOcrModelSet.PPOCRv6Small.DetMean,
        DetStd = RapidOcrModelSet.PPOCRv6Small.DetStd,
        ClsModelPath = AppFiles.Beside(RapidOcrModelSet.PPOCRv5Latin.ClsModelPath),
        RecModelPath = AppFiles.Beside(RapidOcrModelSet.PPOCRv5Latin.RecModelPath),
        KeysPath = AppFiles.Beside(RapidOcrModelSet.PPOCRv5Latin.KeysPath),
    };

    readonly Lock gate = new();
    Engine? engine, retired;
    bool accelerated;

    public void Dispose()
    {
        engine?.Dispose();
        retired?.Dispose();
    }

    // Compiles the detector's shaders for the accelerator before the first import asks for
    // them, on a blank A4 page at the import's resolution.
    public Task Warm() => Task.Run(() =>
    {
        using var blank = new SKBitmap(new SKImageInfo(2480, 3508, SKColorType.Bgra8888, SKAlphaType.Opaque));
        blank.Erase(SKColors.White);
        Read(blank);
    });

    public Task<OcrPage> Recognize(byte[] image, CancellationToken ct) => Task.Run(() =>
    {
        using var decoded = Decode(image);
        return Recognize(decoded);
    }, ct);

    public Task<OcrPage> Recognize(SKBitmap page, CancellationToken ct) => Task.Run(() => Recognize(page), ct);

    public Task<List<OcrWord>> Read(Raster crop, CancellationToken ct) => Task.Run(() =>
    {
        var handle = GCHandle.Alloc(crop.Pixels, GCHandleType.Pinned);
        try
        {
            using var bitmap = new SKBitmap();
            var info = new SKImageInfo(crop.Width, crop.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
            return Words(Read(bitmap));
        }
        finally
        {
            handle.Free();
        }
    }, ct);

    // Show-through goes first, on the original pixels, so it cannot vote on the lean; then
    // the page is straightened and read. A sideways or upside-down page is turned and read
    // again, since the boxes of the first pass sit in the turned frame. The page carries no
    // image: the correction renders it again from the document when it is looked at.
    OcrPage Recognize(SKBitmap decoded)
    {
        using var cleaned = Deink.Apply(decoded);
        var read = cleaned ?? decoded;
        using var straightened = Deskew.Apply(read, out var skew);
        var page = straightened ?? read;
        var correction = new Correction { Scale = (double)read.Width / decoded.Width, Skew = skew };
        var first = Read(page);
        var turn = Turn(first);
        if (turn == 0) return Page(page, first, correction);
        using var turned = Rotate(page, turn);
        using var settled = Deskew.Apply(turned, out var settle);
        var upright = settled ?? turned;
        correction.Turn = turn;
        correction.Settle = settle;
        return Page(upright, Read(upright), correction);
    }

    static OcrPage Page(SKBitmap page, OcrResult result, Correction correction) =>
        new() { Width = page.Width, Height = page.Height, Correction = correction, Words = Words(result) };

    // Pages are read side by side: the accelerator takes one detector run at a time behind
    // its own gate, and everything around it goes to the thread pool, so one page is
    // recognised on the cores while the next is detected. A detector that fails on the
    // accelerator, at load or on a page, is replaced by one on the CPU and the page read
    // again; the failed engine may still be reading another page and is kept until the end.
    OcrResult Read(SKBitmap page)
    {
        var current = Current();
        try
        {
            return current.Detect(page, Options);
        }
        catch (OnnxRuntimeException) when (accelerated)
        {
            return Replace(current).Detect(page, Options);
        }
    }

    Engine Current()
    {
        lock (gate) return engine ??= Open(Accelerator.Available);
    }

    Engine Replace(Engine failed)
    {
        lock (gate)
        {
            if (!ReferenceEquals(engine, failed)) return engine!;
            retired = failed;
            return engine = Open(false);
        }
    }

    // The engine keeps every crop the classifier called upside down, for its verdict, whatever
    // it read. On a page that is not turned that verdict is mostly wrong: a "kg" reads cleanly
    // the right way up. Such a crop keeps its words on the score every other crop is held to;
    // one truly on its head reads as garbage and falls below it.
    internal static List<OcrWord> Words(OcrResult result)
    {
        var words = new List<OcrWord>();
        foreach (var block in result.TextBlocks)
            foreach (var word in Reads(block) ? block.WordResults ?? [] : [])
                if (!string.IsNullOrWhiteSpace(word.Text))
                    words.Add(new OcrWord { Text = word.Text, Box = Bounds(word.BoxPoints), Confidence = word.Score });
        return words;
    }

    static bool Reads(TextBlock block) =>
        block.AngleIndex != 1 || block.CharScores is { Length: > 0 } scores && scores.Average() >= Options.TextScore;

    // Lines running down the page mean a quarter turn; the direction classifier having
    // flipped every crop means upside down. Strict majorities only: turning an upright
    // page costs far more than leaving a sideways one.
    static int Turn(OcrResult result)
    {
        int tall = 0, wide = 0, flipped = 0, upright = 0;
        foreach (var block in result.TextBlocks)
        {
            var box = Bounds(block.BoxPoints);
            if (box.H > box.W) tall++; else wide++;
            if (block.AngleIndex == 1) flipped++;
            else if (block.AngleIndex == 0) upright++;
        }
        return (tall > wide, flipped > upright) switch
        {
            (true, true) => 270,
            (true, false) => 90,
            (false, true) => 180,
            _ => 0,
        };
    }

    Engine Open(bool gpu)
    {
        var engine = new Engine();
        try
        {
            using var detector = gpu ? Accelerator.Session(threads) : Engine.GetDefaultSessionOptions(threads);
            using var reader = Engine.GetDefaultSessionOptions(1);
            // An idle detector thread spinning for work takes its core from the crops read beside it.
            detector.AddSessionConfigEntry("session.intra_op.allow_spinning", "0");
            if (gpu) lock (Accelerator.Gate) engine.InitModels(Models, detector, reader, Accelerator.Gate);
            else engine.InitModels(Models, detector, reader);
        }
        catch (OnnxRuntimeException) when (gpu)
        {
            engine.Dispose();
            return Open(false);
        }
        catch (Exception e)
        {
            engine.Dispose();
            throw new InvalidOperationException(
                "Die Texterkennungsmodelle konnten nicht geladen werden. Erwartet unter " +
                AppFiles.Beside("models") + ".", e);
        }
        accelerated = gpu;
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
    internal static SKBitmap Decode(byte[] image)
    {
        using var data = SKData.CreateCopy(image);
        using var codec = SKCodec.Create(data);
        var decoded = (codec is null ? null : SKBitmap.Decode(codec))
            ?? throw new InvalidOperationException("Das Seitenbild konnte nicht gelesen werden.");
        if (decoded.ColorType == SKColorType.Bgra8888) return decoded;
        using (decoded)
            return decoded.Copy(SKColorType.Bgra8888)
                ?? throw new InvalidOperationException("Das Seitenbild konnte nicht umgewandelt werden.");
    }

    internal static SKBitmap Rotate(SKBitmap source, int degrees)
    {
        var (map, size) = Turning(new SKSizeI(source.Width, source.Height), degrees);
        var turned = new SKBitmap(new SKImageInfo(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul));
        using var canvas = new SKCanvas(turned);
        canvas.Concat(map);
        canvas.DrawBitmap(source, 0, 0);
        return turned;
    }

    // Exact quarter turns: pixel centres land on pixel centres and nothing is resampled.
    internal static (SKMatrix Map, SKSizeI Size) Turning(SKSizeI page, int degrees)
    {
        var swap = degrees != 180;
        var width = swap ? page.Height : page.Width;
        var height = swap ? page.Width : page.Height;
        var (cos, sin) = degrees switch { 90 => (0, 1), 180 => (-1, 0), _ => (0, -1) };
        var map = new SKMatrix(cos, -sin, degrees == 270 ? 0 : width, sin, cos, degrees == 90 ? 0 : height, 0, 0, 1);
        return (map, new SKSizeI(width, height));
    }
}

// The WebGPU plugin: DirectX 12 on Windows, Metal on macOS, loaded beside the CPU runtime
// the tagger is pinned to. No adapter, no library, or a runtime that refuses it all mean CPU,
// and so does UMSATZSCHAETZUNG_CPU=1 for machines whose only adapter is a software one.
// The device takes one session at a time, at load and per run: the corpus tool runs a
// reader per core and they all queue here for the detector.
static class Accelerator
{
    static readonly Lazy<OrtEpDevice?> device = new(Find);

    public static readonly object Gate = new();

    public static bool Available => device.Value is not null;

    // The device's buffer cache keeps every page size it has seen, gigabytes of them in unified
    // memory; without it a page reads as fast.
    public static SessionOptions Session(int threads)
    {
        var options = Engine.GetDefaultSessionOptions(threads);
        if (device.Value is { } gpu) options.AppendExecutionProvider(OrtEnv.Instance(), [gpu], new Dictionary<string, string> { ["storageBufferCacheMode"] = "disabled" });
        return options;
    }

    static OrtEpDevice? Find()
    {
        if (Environment.GetEnvironmentVariable("UMSATZSCHAETZUNG_CPU") == "1") return null;
        try
        {
            var env = OrtEnv.Instance();
            env.RegisterExecutionProviderLibrary("webgpu", WebGpuEp.GetLibraryPath());
            return env.GetEpDevices().FirstOrDefault(d => d.EpName == WebGpuEp.GetEpName());
        }
        catch (Exception e) when (e is OnnxRuntimeException or IOException or PlatformNotSupportedException or DllNotFoundException)
        {
            return null;
        }
    }
}

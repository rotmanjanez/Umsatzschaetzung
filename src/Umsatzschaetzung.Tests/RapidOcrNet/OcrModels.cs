using System.Collections.Concurrent;
using RapidOcrNet;
using SkiaSharp;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

// The models the app ships: the v6 small detector with the v5 classifier and latin recogniser.
// Loaded once, on the CPU, so the readings do not depend on the machine's accelerator.
public sealed class OcrModels : IDisposable
{
    static string Model(string version, string file) => Path.Combine(AppContext.BaseDirectory, "models", version, file);

    public static readonly RapidOcrModelSet Set = RapidOcrModelSet.PPOCRv5Latin with
    {
        DetModelPath = Model("v6", "PP-OCRv6_det_small.onnx"),
        DetMean = RapidOcrModelSet.PPOCRv6Small.DetMean,
        DetStd = RapidOcrModelSet.PPOCRv6Small.DetStd,
        ClsModelPath = Model("v5", "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
        RecModelPath = Model("v5", "latin_PP-OCRv5_rec_mobile_infer.onnx"),
        KeysPath = Model("v5", "ppocrv5_latin_dict.txt"),
    };

    public static readonly RapidOcrOptions Upstream = RapidOcrOptions.PPOCRv6 with { ReturnWordBox = true };

    public static readonly RapidOcrOptions App = Upstream with
    {
        MaxSideLen = 4000,
        ClsRotate = false,
        RotateTallCrops = false,
        SplitStackedCrops = true,
    };

    public RapidOcr Engine { get; } = new();
    public TextDetector Detector { get; } = new();
    public TextClassifier Classifier { get; } = new();
    public TextRecognizer Recognizer { get; } = new();

    public static readonly string[] Lines = ["Rechnung Nr. 4711", "Menge 12 Stk", "Summe 1.234,56 €"];
    public static readonly int[] Baselines = [60, 140, 220];

    // Black on white at 40 px, the size of a line on a page scanned at 300 dpi.
    public SKBitmap Page { get; } = Images.Text(480, 260, 40, [.. Lines.Select((l, i) => (l, 20f, (float)Baselines[i]))]);
    public SKBitmap Turned { get; }

    readonly ConcurrentDictionary<(bool, RapidOcrOptions), OcrResult> reads = new();

    public OcrResult Read(bool turned, RapidOcrOptions options) =>
        reads.GetOrAdd((turned, options), key => Engine.Detect(key.Item1 ? Turned : Page, key.Item2));

    public OcrModels()
    {
        using var options = RapidOcr.GetDefaultSessionOptions();
        Engine.InitModels(Set, options);
        Detector.InitModel(Set.DetModelPath, Set.DetMean, Set.DetStd, options);
        Classifier.InitModel(Set.ClsModelPath, options);
        Recognizer.InitModel(Set.RecModelPath, Set.KeysPath, options);
        Turned = Images.Turned180(Page);
    }

    public void Dispose()
    {
        Engine.Dispose();
        Detector.Dispose();
        Classifier.Dispose();
        Recognizer.Dispose();
        Page.Dispose();
        Turned.Dispose();
    }
}

[CollectionDefinition(Name)]
public sealed class OcrCollection : ICollectionFixture<OcrModels>
{
    public const string Name = "RapidOcr models";
}

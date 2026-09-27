using System.Collections.Concurrent;
using RapidOcrNet;
using SkiaSharp;
using Umsatzschaetzung.Nets;

namespace Umsatzschaetzung.Tests.RapidOcrNet;

// The models the app ships: the v6 small detector with the v5 classifier and latin recogniser.
// Loaded once, on the CPU, so the readings do not depend on the machine's accelerator.
public sealed class OcrModels : IAsyncLifetime
{
    static readonly OrtWeights Weights = new(Path.Combine(AppContext.BaseDirectory, "models"));

    static readonly NetOptions Cpu = new(Threads: 0, InterThreads: 0, Extended: true);

    public static readonly RapidOcrModelSet Set = RapidOcrModelSet.PPOCRv5Latin with
    {
        DetModelPath = RapidOcrModelSet.PPOCRv6Small.DetModelPath,
        DetMean = RapidOcrModelSet.PPOCRv6Small.DetMean,
        DetStd = RapidOcrModelSet.PPOCRv6Small.DetStd,
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

    readonly ConcurrentDictionary<(bool, RapidOcrOptions), Lazy<Task<OcrResult>>> reads = new();

    public Task<OcrResult> Read(bool turned, RapidOcrOptions options) =>
        reads.GetOrAdd((turned, options), key => new(() => Engine.Detect(key.Item1 ? Turned : Page, key.Item2))).Value;

    public OcrModels()
    {
        Turned = Images.Turned180(Page);
    }

    public async ValueTask InitializeAsync()
    {
        await Engine.InitModels(Weights, Set, Cpu, Cpu);
        Detector.InitModel(await Weights.Open(Set.DetModelPath, Cpu), Set.DetMean, Set.DetStd);
        Classifier.InitModel(await Weights.Open(Set.ClsModelPath, Cpu));
        Recognizer.InitModel(await Weights.Open(Set.RecModelPath, Cpu), await Weights.Read(Set.KeysPath));
    }

    public ValueTask DisposeAsync()
    {
        Engine.Dispose();
        Detector.Dispose();
        Classifier.Dispose();
        Recognizer.Dispose();
        Page.Dispose();
        Turned.Dispose();
        return ValueTask.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class OcrCollection : ICollectionFixture<OcrModels>
{
    public const string Name = "RapidOcr models";
}

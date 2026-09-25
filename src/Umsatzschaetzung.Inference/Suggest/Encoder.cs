using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Suggest;

// Bi-encoder over article wordings, int8 ONNX. The graph mean-pools and L2-normalises,
// so the similarity of two wordings is the dot product of their vectors. Its text side
// is the tagger's, down to the vocabulary, so the tokenizer is read from the tagger's
// model directory.
public sealed class Encoder : IEncoder, IDisposable
{
    public const string Name = "zuordnung-0.1.2/int8";
    const int Width = IEncoder.Width;

    public string Model => Name;

    // An article wording is a handful of words; 48 tokens hold the longest of them whole.
    const int MaxLen = 48;
    const int Batch = 64;

    // Eight threads measured slower than four on an M1 Pro, as for the tagger.
    const int Threads = 4;

    static string Dir => AppFiles.Beside(Path.Combine("models", "zuordnung"));
    static string ModelPath => Path.Combine(Dir, "zuordnung.int8.onnx");
    static string CalibrationPath => Path.Combine(Dir, "calibration.json");
    static string TokenizerDir => AppFiles.Beside(Path.Combine("models", "belegtagger"));

    readonly Lock gate = new();
    // Not InferenceSession: naming it in Dispose loads OnnxRuntime on every shutdown.
    IDisposable? session;
    Bpe? bpe;
    (double A, double B)? fit;

    public void Dispose() => session?.Dispose();

    // A cosine is not a probability. The logistic fit that turns one into the other was
    // measured on held-out pairs and ships with the weights.
    public int Confidence(double cos)
    {
        var (a, b) = Fit();
        return Math.Clamp((int)Math.Round(100 / (1 + Math.Exp(-(a * cos + b)))), 1, 99);
    }

    // Die Aktivierungen werden zur Laufzeit je Tensor quantisiert, also verschiebt jede
    // Auffüllung eines Stapels die Werte aller seiner Texte — gleich lange Texte in einem
    // Stapel halten die Auffüllung klein und die Einbettung nahe an der des Textes allein.
    public float[][] Embed(IReadOnlyList<string> texts)
    {
        var vectors = new float[texts.Count][];
        lock (gate)
        {
            bpe ??= Bpe.Open(TokenizerDir);
            session ??= Open();
            var rows = new int[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
            {
                var body = bpe.Encode(texts[i]);
                rows[i] = body.Length > MaxLen - 2 ? body[..(MaxLen - 2)] : body;
            }
            var order = Enumerable.Range(0, texts.Count).OrderBy(i => rows[i].Length).ToArray();
            for (var at = 0; at < order.Length; at += Batch)
                Run((InferenceSession)session, bpe, rows, order[at..Math.Min(at + Batch, order.Length)], vectors);
        }
        return vectors;
    }

    (double A, double B) Fit()
    {
        lock (gate) return fit ??= ReadFit();
    }

    static (double, double) ReadFit()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllBytes(CalibrationPath));
            return (doc.RootElement.GetProperty("a").GetDouble(), doc.RootElement.GetProperty("b").GetDouble());
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                "Die Kalibrierung der Artikelzuordnung konnte nicht gelesen werden. Erwartet unter " + CalibrationPath + ".", e);
        }
    }

    static InferenceSession Open()
    {
        var options = new SessionOptions { IntraOpNumThreads = Threads, InterOpNumThreads = 1 };
        try
        {
            var session = new InferenceSession(ModelPath, options);
            if (!session.OutputMetadata.TryGetValue("embedding", out var meta))
                throw new InvalidOperationException("Das Zuordnungsmodell liefert keinen Ausgang \"embedding\".");
            if (meta.Dimensions[^1] != Width)
                throw new InvalidOperationException($"Das Zuordnungsmodell liefert keine {Width} Werte je Text.");
            return session;
        }
        catch (Exception e)
        {
            options.Dispose();
            throw new InvalidOperationException(
                "Das Modell zur Artikelzuordnung konnte nicht geladen werden. Erwartet unter " + ModelPath + ".", e);
        }
    }

    static void Run(InferenceSession session, Bpe bpe, int[][] rows, int[] batch, float[][] into)
    {
        var n = batch.Length;
        var len = 2 + batch.Max(i => rows[i].Length);
        var ids = new DenseTensor<long>([n, len]);
        var mask = new DenseTensor<long>([n, len]);
        for (var r = 0; r < n; r++)
        {
            var body = rows[batch[r]];
            ids[r, 0] = bpe.Bos;
            for (var k = 0; k < body.Length; k++) ids[r, k + 1] = body[k];
            ids[r, body.Length + 1] = bpe.Eos;
            for (var k = body.Length + 2; k < len; k++) ids[r, k] = bpe.Pad;
            for (var k = 0; k < body.Length + 2; k++) mask[r, k] = 1;
        }

        using var results = session.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", ids),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        ]);
        foreach (var result in results)
        {
            if (result.Name != "embedding") continue;
            var flat = result.AsTensor<float>().ToArray();
            for (var r = 0; r < n; r++) into[batch[r]] = flat[(r * Width)..((r + 1) * Width)];
            return;
        }
        throw new InvalidOperationException("Das Zuordnungsmodell liefert keinen Ausgang \"embedding\".");
    }
}

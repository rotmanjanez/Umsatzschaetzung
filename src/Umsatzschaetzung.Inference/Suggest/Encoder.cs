using System.Text.Json;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Suggest;

// Bi-encoder over article wordings, int8 ONNX. The graph mean-pools and L2-normalises,
// so the similarity of two wordings is the dot product of their vectors. Its text side
// is the tagger's, down to the vocabulary, so the tokenizer is read from the tagger's
// model directory.
public sealed class Encoder(IWeights weights) : IEncoder, IDisposable
{
    public const string Name = "zuordnung-0.1.2/int8";
    const int Width = IEncoder.Width;

    public string Model => Name;

    // An article wording is a handful of words; 48 tokens hold the longest of them whole.
    const int MaxLen = 48;
    const int Batch = 64;

    const string ModelFile = "zuordnung/zuordnung.int8.onnx";
    const string CalibrationFile = "zuordnung/calibration.json";
    const string TokenizerDir = "belegtagger";

    // Eight threads measured slower than four on an M1 Pro, and no spinning, as for the tagger.
    static readonly NetOptions Options = new(Threads: 4, Spin: false);

    readonly SemaphoreSlim gate = new(1, 1);
    INet? net;
    Bpe? bpe;
    Fit? fit;

    sealed record Fit(double A, double B);

    public void Dispose() => net?.Dispose();

    public async Task Load(CancellationToken ct = default)
    {
        if (fit is not null) return;
        await gate.WaitAsync(ct);
        try
        {
            fit ??= await ReadFit(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    // A cosine is not a probability. The logistic fit that turns one into the other was
    // measured on held-out pairs and ships with the weights.
    public int Confidence(double cos)
    {
        var (a, b) = fit ?? throw new InvalidOperationException("Die Kalibrierung der Artikelzuordnung ist noch nicht geladen.");
        return Math.Clamp((int)Math.Round(100 / (1 + Math.Exp(-(a * cos + b)))), 1, 99);
    }

    // Die Aktivierungen werden zur Laufzeit je Tensor quantisiert, also verschiebt jede
    // Auffüllung eines Stapels die Werte aller seiner Texte — gleich lange Texte in einem
    // Stapel halten die Auffüllung klein und die Einbettung nahe an der des Textes allein.
    public async Task<float[][]> Embed(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new float[texts.Count][];
        await gate.WaitAsync(ct);
        try
        {
            bpe ??= await Bpe.Open(weights, TokenizerDir, ct);
            net ??= await Open(ct);
            var rows = new int[texts.Count][];
            for (var i = 0; i < texts.Count; i++)
            {
                var body = bpe.Encode(texts[i]);
                rows[i] = body.Length > MaxLen - 2 ? body[..(MaxLen - 2)] : body;
            }
            var order = Enumerable.Range(0, texts.Count).OrderBy(i => rows[i].Length).ToArray();
            for (var at = 0; at < order.Length; at += Batch)
                await Run(net, bpe, rows, order[at..Math.Min(at + Batch, order.Length)], vectors, ct);
        }
        finally
        {
            gate.Release();
        }
        return vectors;
    }

    async Task<Fit> ReadFit(CancellationToken ct)
    {
        try
        {
            using var doc = JsonDocument.Parse(await weights.Read(CalibrationFile, ct));
            return new Fit(doc.RootElement.GetProperty("a").GetDouble(), doc.RootElement.GetProperty("b").GetDouble());
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                "Die Kalibrierung der Artikelzuordnung konnte nicht gelesen werden. Erwartet unter models/" + CalibrationFile + ": " + e.Message, e);
        }
    }

    async Task<INet> Open(CancellationToken ct)
    {
        INet? opened = null;
        try
        {
            opened = await weights.Open(ModelFile, Options, ct);
            var port = opened.Outputs.FirstOrDefault(p => p.Name == "embedding")
                ?? throw new InvalidOperationException("Das Zuordnungsmodell liefert keinen Ausgang \"embedding\".");
            if (port.Shape[^1] != Width)
                throw new InvalidOperationException($"Das Zuordnungsmodell liefert keine {Width} Werte je Text.");
            return opened;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            opened?.Dispose();
            throw new InvalidOperationException(
                "Das Modell zur Artikelzuordnung konnte nicht geladen werden. Erwartet unter models/" + ModelFile + ": " + e.Message, e);
        }
    }

    static async Task Run(INet net, Bpe bpe, int[][] rows, int[] batch, float[][] into, CancellationToken ct)
    {
        var n = batch.Length;
        var len = 2 + batch.Max(i => rows[i].Length);
        var ids = new long[n * len];
        var mask = new long[n * len];
        for (var r = 0; r < n; r++)
        {
            var body = rows[batch[r]];
            var row = r * len;
            ids[row] = bpe.Bos;
            for (var k = 0; k < body.Length; k++) ids[row + k + 1] = body[k];
            ids[row + body.Length + 1] = bpe.Eos;
            for (var k = body.Length + 2; k < len; k++) ids[row + k] = bpe.Pad;
            for (var k = 0; k < body.Length + 2; k++) mask[row + k] = 1;
        }

        var results = await net.Run([Tensor.Of("input_ids", ids, n, len), Tensor.Of("attention_mask", mask, n, len)], ct);
        var flat = results.Named("embedding").F;
        for (var r = 0; r < n; r++) into[batch[r]] = flat[(r * Width)..((r + 1) * Width)];
    }
}

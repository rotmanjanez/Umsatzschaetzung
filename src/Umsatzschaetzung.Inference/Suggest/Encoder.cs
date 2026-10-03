using System.Runtime.ExceptionServices;
using System.Text.Json;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Suggest;

// Bi-encoder over article wordings, int8 ONNX. The graph mean-pools and L2-normalises,
// so the similarity of two wordings is the dot product of their vectors. Its text side
// is the tagger's, down to the vocabulary, so the tokenizer is read from the tagger's
// model directory.
//
// The graph quantises its activations with one scale per tensor, so a text batched with others
// and their padding moves with them: each text runs alone, and its vector is the same, bit for
// bit, whatever it is embedded with. Runs above one run that many texts at once on a thread
// each, for a runtime whose runs may overlap and return finished: the caller's thread takes
// part and the call returns finished too, so no continuation waits for a dispatcher that is
// itself waiting, as the headless driver's is at its end.
public sealed class Encoder(IWeights weights, int runs = 1) : IEncoder, IDisposable
{
    public const string Name = "zuordnung-0.1.2/int8/alone";
    const int Width = IEncoder.Width;

    public string Model => Name;

    // An article wording is a handful of words; 48 tokens hold the longest of them whole.
    const int MaxLen = 48;

    const string ModelFile = "zuordnung/zuordnung.int8.onnx";
    const string CalibrationFile = "zuordnung/calibration.json";
    const string TokenizerDir = "belegtagger";

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

    public async Task<float[][]> Embed(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        var vectors = new float[texts.Count][];
        await gate.WaitAsync(ct);
        try
        {
            var tokens = bpe ??= await Bpe.Open(weights, TokenizerDir, ct);
            var model = net ??= await Open(ct);
            if (runs == 1 || texts.Count == 1)
                for (var i = 0; i < texts.Count; i++) vectors[i] = await Run(model, tokens, texts[i], ct);
            else
                try
                {
                    Parallel.For(0, texts.Count, new ParallelOptions { MaxDegreeOfParallelism = runs, CancellationToken = ct },
                        i => vectors[i] = Run(model, tokens, texts[i], ct).GetAwaiter().GetResult());
                }
                catch (AggregateException e)
                {
                    ExceptionDispatchInfo.Throw(e.InnerExceptions[0]);
                }
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
            // Eight threads measured slower than four on an M1 Pro, and no spinning, as for the tagger.
            // Texts run side by side get two each.
            opened = await weights.Open(ModelFile, new NetOptions(Threads: runs == 1 ? 4 : 2, Spin: false), ct);
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

    static async Task<float[]> Run(INet net, Bpe bpe, string text, CancellationToken ct)
    {
        var body = bpe.Encode(text);
        if (body.Length > MaxLen - 2) body = body[..(MaxLen - 2)];
        var len = body.Length + 2;
        var ids = new long[len];
        ids[0] = bpe.Bos;
        for (var k = 0; k < body.Length; k++) ids[k + 1] = body[k];
        ids[^1] = bpe.Eos;
        var mask = new long[len];
        Array.Fill(mask, 1L);

        var results = await net.Run([Tensor.Of("input_ids", ids, 1, len), Tensor.Of("attention_mask", mask, 1, len)], ct);
        return results.Named("embedding").F;
    }
}

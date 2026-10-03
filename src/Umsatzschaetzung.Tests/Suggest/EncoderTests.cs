using System.Text.Json;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public sealed class EncoderFixture : IDisposable
{
    public Encoder Encoder { get; } = new(new OrtWeights(AppFiles.Beside("models")));

    public void Dispose() => Encoder.Dispose();
}

public class EncoderTests(EncoderFixture f) : IClassFixture<EncoderFixture>
{
    // int8 kernels are not bit-portable across CPUs and ORT versions.
    const double ParityCosine = 0.98;

    // A drift in the tokenizer or the graph costs accuracy without ever crashing.
    [Fact]
    public async Task TheVectorsTheWeightsWereExportedWithAreReproduced()
    {
        var worst = 1.0;
        var lines = 0;
        foreach (var l in File.ReadLines(TestData.File("parity.jsonl")))
        {
            if (l.Trim() == "") continue;
            using var doc = JsonDocument.Parse(l);
            var want = doc.RootElement.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            var got = (await f.Encoder.Embed([doc.RootElement.GetProperty("text").GetString()!], TestContext.Current.CancellationToken))[0];
            Assert.Equal(IEncoder.Width, got.Length);
            worst = Math.Min(worst, Vec.Dot(want, got));
            lines++;
        }
        Assert.True(lines > 0 && worst >= ParityCosine, $"worst cosine {worst:F5} over {lines} texts");
    }

    [Fact]
    public async Task EveryVectorHasUnitLength()
    {
        foreach (var v in await f.Encoder.Embed(["Fassbier Pils, Keg 50 l", "", "ÄÖÜ ß €", new string('x', 500)], TestContext.Current.CancellationToken))
            Assert.Equal(1.0, Math.Sqrt(Vec.Dot(v, v)), 2);
    }

    [Fact]
    public async Task NothingToEmbedIsNoVectors() => Assert.Empty(await f.Encoder.Embed([], TestContext.Current.CancellationToken));

    [Fact]
    public async Task ALongWordingIsCutAtTheWindowNotRejected()
    {
        var head = string.Join(' ', Enumerable.Range(0, 30).Select(i => "Pils" + i));
        var v = await f.Encoder.Embed([head + " Zusatz", head + " ganz anders, sehr viel länger als das Fenster erlaubt"], TestContext.Current.CancellationToken);
        Assert.Equal(v[0], v[1]);
    }

    // The graph's activation scales span its whole input, so a text batched with others moved
    // by up to 0.013 in cosine, enough to flip a confidence across the threshold.
    [Fact]
    public async Task AVectorIsTheSameWhateverItIsEmbeddedWith()
    {
        var ct = TestContext.Current.CancellationToken;
        var texts = Enumerable.Range(0, 70).Select(i => i % 2 == 0 ? $"Pils {i}" : $"Doppelkorn Flasche 0,7 l Nr. {i}").ToList();
        var all = await f.Encoder.Embed(texts, ct);
        using var parallel = new Encoder(new OrtWeights(AppFiles.Beside("models")), runs: 4);
        var reversed = await parallel.Embed(texts.AsEnumerable().Reverse().ToList(), ct);
        for (var i = 0; i < texts.Count; i++)
        {
            var alone = (await f.Encoder.Embed([texts[i]], ct))[0];
            Assert.Equal(alone, all[i]);
            Assert.Equal(alone, reversed[texts.Count - 1 - i]);
        }
    }

    sealed class Unpumped : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) { }
    }

    // The headless driver awaits its last embedding on a dispatcher no one pumps any more.
    [Fact]
    public async Task ParallelRunsReturnFinishedOnAContextNoOnePumps()
    {
        using var parallel = new Encoder(new OrtWeights(AppFiles.Beside("models")), runs: 4);
        await parallel.Embed([], TestContext.Current.CancellationToken);
        var before = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new Unpumped());
        try
        {
            var run = parallel.Embed([.. Enumerable.Range(0, 16).Select(i => $"Pils {i}")], TestContext.Current.CancellationToken);
            Assert.True(run.IsCompleted);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(before);
        }
    }

    // A net that is cancelled on its third run, as the runtime is when the program shuts down.
    sealed class Cancelling(CancellationTokenSource cts) : IWeights, INet
    {
        readonly OrtWeights real = new(AppFiles.Beside("models"));
        int ran;

        public bool Accelerated => false;
        public IReadOnlyList<Port> Inputs => [];
        public IReadOnlyList<Port> Outputs => [new("embedding", [-1, IEncoder.Width])];

        public Task<INet> Open(string model, NetOptions options, CancellationToken ct = default) => Task.FromResult<INet>(this);
        public Task<byte[]> Read(string file, CancellationToken ct = default) => real.Read(file, ct);

        public Task<Tensor[]> Run(IReadOnlyList<Tensor> inputs, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref ran) == 3) cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult<Tensor[]>([Tensor.Of("embedding", new float[IEncoder.Width], 1, IEncoder.Width)]);
        }

        public void Dispose() { }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task ACancelledRunIsACancellation(int runs)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var encoder = new Encoder(new Cancelling(cts), runs);
        var texts = Enumerable.Range(0, 64).Select(i => $"Pils {i}").ToList();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => encoder.Embed(texts, cts.Token));
    }

    [Fact]
    public async Task ConfidenceStaysBetweenOneAndNinetyNineAndRisesWithTheCosine()
    {
        await f.Encoder.Load(TestContext.Current.CancellationToken);
        var steps = Enumerable.Range(-20, 61).Select(i => f.Encoder.Confidence(i / 20.0)).ToList();
        Assert.All(steps, c => Assert.InRange(c, 1, 99));
        Assert.Equal(steps.Order(), steps);
        Assert.Equal((1, 99), (f.Encoder.Confidence(-1), f.Encoder.Confidence(double.MaxValue)));
    }
}

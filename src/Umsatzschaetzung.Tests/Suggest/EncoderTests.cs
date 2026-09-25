using System.Runtime.InteropServices;
using System.Text.Json;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public sealed class EncoderFixture : IDisposable
{
    public Encoder Encoder { get; } = new();

    public void Dispose() => Encoder.Dispose();
}

public class EncoderTests(EncoderFixture f) : IClassFixture<EncoderFixture>
{
    // The fixture was measured on Apple silicon; int8 kernels are not bit-portable, and on
    // x86 the same vectors come out about 0.98 apart.
    static readonly double ParityCosine =
        OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? 0.999 : 0.98;

    // A drift in the tokenizer or the graph costs accuracy without ever crashing. Each
    // text is embedded alone, as the fixture was: inside a padded batch the per-tensor
    // activation scales move it by about 0.988.
    [Fact]
    public void TheVectorsTheWeightsWereExportedWithAreReproduced()
    {
        var worst = 1.0;
        var lines = 0;
        foreach (var l in File.ReadLines(TestData.File("parity.jsonl")))
        {
            if (l.Trim() == "") continue;
            using var doc = JsonDocument.Parse(l);
            var want = doc.RootElement.GetProperty("embedding").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            var got = f.Encoder.Embed([doc.RootElement.GetProperty("text").GetString()!])[0];
            Assert.Equal(IEncoder.Width, got.Length);
            worst = Math.Min(worst, Vec.Dot(want, got));
            lines++;
        }
        Assert.True(lines > 0 && worst >= ParityCosine, $"worst cosine {worst:F5} over {lines} texts");
    }

    [Fact]
    public void EveryVectorHasUnitLength()
    {
        foreach (var v in f.Encoder.Embed(["Fassbier Pils, Keg 50 l", "", "ÄÖÜ ß €", new string('x', 500)]))
            Assert.Equal(1.0, Math.Sqrt(Vec.Dot(v, v)), 2);
    }

    [Fact]
    public void NothingToEmbedIsNoVectors() => Assert.Empty(f.Encoder.Embed([]));

    [Fact]
    public void ALongWordingIsCutAtTheWindowNotRejected()
    {
        var head = string.Join(' ', Enumerable.Range(0, 30).Select(i => "Pils" + i));
        var v = f.Encoder.Embed([head + " Zusatz", head + " ganz anders, sehr viel länger als das Fenster erlaubt"]);
        Assert.Equal(v[0], v[1]);
    }

    // Texts are batched by length and in batches of 64; each vector still comes back at the
    // position of its text.
    [Fact]
    public void VectorsComeBackInTheOrderOfTheirTexts()
    {
        var texts = Enumerable.Range(0, 70).Select(i => i % 2 == 0 ? $"Pils {i}" : $"Doppelkorn Flasche 0,7 l Nr. {i}").ToList();
        var all = f.Encoder.Embed(texts);
        foreach (var i in new[] { 0, 1, 64, 69 })
        {
            var alone = f.Encoder.Embed([texts[i]])[0];
            var own = Vec.Dot(alone, all[i]);
            Assert.True(own >= 0.98, $"{i}: {own:F4}");
            Assert.True(own > Vec.Dot(alone, all[(i + 1) % texts.Count]), $"{i} closer to its neighbour");
        }
    }

    [Fact]
    public void ConfidenceStaysBetweenOneAndNinetyNineAndRisesWithTheCosine()
    {
        var steps = Enumerable.Range(-20, 61).Select(i => f.Encoder.Confidence(i / 20.0)).ToList();
        Assert.All(steps, c => Assert.InRange(c, 1, 99));
        Assert.Equal(steps.Order(), steps);
        Assert.Equal((1, 99), (f.Encoder.Confidence(-1), f.Encoder.Confidence(double.MaxValue)));
    }
}

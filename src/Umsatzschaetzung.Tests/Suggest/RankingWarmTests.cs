using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

// A warmed ranking has indexed the wares and, eager, opened the model: the first line asked about
// embeds itself and nothing else. Not eager, a line already in the cache asks the model for nothing.
public class RankingWarmTests
{
    sealed class Counting : IEncoder
    {
        public List<int> Calls { get; } = [];
        public string Model => "zählend";

        public Task<float[][]> Embed(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            Calls.Add(texts.Count);
            return Task.FromResult(texts.Select(_ => Vec.Axis(0)).ToArray());
        }

        public Task Load(CancellationToken ct = default) => Task.CompletedTask;
        public int Confidence(double cos) => 50;
    }

    sealed class Kept : IEmbeddingCache
    {
        readonly Dictionary<string, float[]> rows = [];

        public Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts) =>
            texts.Where(rows.ContainsKey).ToDictionary(t => t, t => rows[t]);

        public void Write(string model, IReadOnlyList<(string Text, float[] Vec)> written)
        {
            foreach (var (text, vec) in written) rows[text] = vec;
        }
    }

    static RuleSet Wares()
    {
        var rs = new RuleSet { Version = 1 };
        rs.Put(new Ingredient { Id = "ing.a", Name = "A" });
        rs.Put(new Ingredient { Id = "ing.b", Name = "B" });
        return rs;
    }

    static readonly DateOnly Day = new(2025, 1, 1);

    [Fact]
    public async Task AWarmedRankingEmbedsOnlyTheLine()
    {
        var ct = TestContext.Current.CancellationToken;
        var encoder = new Counting();
        var ranking = new EncoderRanking(encoder);

        await ranking.Warm(Wares(), "", ct);
        Assert.Equal([2, 0], encoder.Calls);

        await ranking.Rank(Wares(), "", new InvoiceLine { Name = "Ware" }, Day, 5, ct);
        Assert.Equal([2, 0, 1], encoder.Calls);
    }

    [Fact]
    public async Task ALineInTheCacheAsksTheModelForNothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var cache = new Kept();
        await new EncoderRanking(new Counting(), cache).Warm(Wares(), "", ct);
        cache.Write("zählend", [("Pils", Vec.Axis(0))]);

        var encoder = new Counting();
        var ranking = new EncoderRanking(encoder, cache, eager: false);
        await ranking.Warm(Wares(), "", ct);
        await ranking.Rank(Wares(), "", new InvoiceLine { Name = "Pils" }, Day, 5, ct);
        Assert.Empty(encoder.Calls);
    }
}

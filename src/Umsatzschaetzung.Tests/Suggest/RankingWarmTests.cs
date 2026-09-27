using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

// A warmed ranking has indexed the wares and opened the model: the first line asked about embeds itself and nothing else.
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

    [Fact]
    public async Task AWarmedRankingEmbedsOnlyTheLine()
    {
        var encoder = new Counting();
        var ranking = new EncoderRanking(encoder);
        var rs = new RuleSet { Version = 1 };
        rs.Put(new Ingredient { Id = "ing.a", Name = "A" });
        rs.Put(new Ingredient { Id = "ing.b", Name = "B" });

        await ranking.Warm(rs, "", TestContext.Current.CancellationToken);
        Assert.Equal([2, 0], encoder.Calls);

        await ranking.Rank(rs, "", new InvoiceLine { Name = "Ware" }, new DateOnly(2025, 1, 1), 5, TestContext.Current.CancellationToken);
        Assert.Equal([2, 0, 1], encoder.Calls);
    }
}

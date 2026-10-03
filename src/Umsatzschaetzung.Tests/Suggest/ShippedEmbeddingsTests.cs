using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

// The shipped embeddings are warmed again only when a file Embeddings.targets hashes changes. They
// hold every wording of the seed's wares: a change outside those files that alters the wordings
// fails here, and needs one of them touched so the embeddings are warmed again.
public class ShippedEmbeddingsTests
{
    sealed class Asked : IEncoder
    {
        public List<string> Texts { get; } = [];
        public string Model => Encoder.Name;

        public Task<float[][]> Embed(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            Texts.AddRange(texts);
            return Task.FromResult(texts.Select(_ => new float[IEncoder.Width]).ToArray());
        }

        public Task Load(CancellationToken ct = default) => Task.CompletedTask;
        public int Confidence(double cos) => 50;
    }

    [Fact]
    public async Task HoldEveryWordingOfTheSeed()
    {
        var asked = new Asked();
        await new Wares(asked).Index(RuleStore.Seed(), "", TestContext.Current.CancellationToken);

        using var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = AppFiles.Beside(EmbeddingStore.Shipped),
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT text FROM embedding WHERE model = @model";
        cmd.Parameters.AddWithValue("@model", Encoder.Name);
        var shipped = new List<string>();
        using (var rows = cmd.ExecuteReader())
            while (rows.Read()) shipped.Add(rows.GetString(0));

        Assert.Equal(asked.Texts.Order(StringComparer.Ordinal), shipped.Order(StringComparer.Ordinal));
    }
}

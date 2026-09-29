using Umsatzschaetzung.Model;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Browser;

// The browser runs the encoder at a fraction of native speed, and a first index over the
// shipped rules would wait minutes for it: their embedding cache is warmed when the page is
// published and fetched with the weights the first time a line is ranked.
sealed class Prewarmed(IWeights weights, string dir, Func<IRanking> open) : IRanking
{
    public const string File = "seed/embeddings.db";

    Task<IRanking>? ranking;

    public async Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default) =>
        await (await (ranking ??= Open())).Rank(rs, gewerbe, line, date, count, ct);

    public async Task Warm(RuleSet rs, string gewerbe, CancellationToken ct = default) =>
        await (await (ranking ??= Open())).Warm(rs, gewerbe, ct);

    async Task<IRanking> Open()
    {
        var path = Path.Combine(dir, "embeddings.db");
        if (!System.IO.File.Exists(path))
        {
            await System.IO.File.WriteAllBytesAsync(path + ".part", await weights.Read(File));
            System.IO.File.Move(path + ".part", path);
        }
        return open();
    }
}

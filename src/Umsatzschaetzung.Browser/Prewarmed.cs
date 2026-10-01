using Umsatzschaetzung.Model;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Browser;

// The browser runs the encoder at a fraction of native speed, and a first index over the
// shipped rules would wait minutes for it: their embedding cache is warmed when the page is
// published and fetched with the weights the first time a line is ranked. It is laid out beside
// /work, not in it, so the stores keep only what was embedded here and a new release's cache
// takes the old one's place.
sealed class Prewarmed(IWeights weights, Func<string, IRanking> open) : IRanking
{
    public const string File = "seed/embeddings.db";
    const string Seed = "/seed/embeddings.db";

    Task<IRanking>? ranking;

    public async Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default) =>
        await (await (ranking ??= Open())).Rank(rs, gewerbe, line, date, count, ct);

    public async Task Warm(RuleSet rs, string gewerbe, CancellationToken ct = default) =>
        await (await (ranking ??= Open())).Warm(rs, gewerbe, ct);

    // A failed fetch is tried again by the next line asked about.
    async Task<IRanking> Open()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Seed)!);
            await System.IO.File.WriteAllBytesAsync(Seed, await weights.Read(File));
            return open(Seed);
        }
        catch
        {
            ranking = null;
            throw;
        }
    }
}

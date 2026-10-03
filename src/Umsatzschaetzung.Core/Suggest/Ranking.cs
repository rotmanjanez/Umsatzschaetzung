using System.Numerics;
using System.Runtime.InteropServices;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

public sealed record Ranked(string ProductId, int Confidence);

// Which wares a line reads like, best first, and how sure: the part of a suggestion a model
// answers. The exact hit, the cut and the mapping are the matcher's, whoever ranks.
public interface IRanking
{
    Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default);

    // Pays ahead what the first Rank would: the wares indexed and, eager, the model opened. A ranking
    // that pays dearly for it may wait while the case has no line open.
    Task Warm(RuleSet rs, string gewerbe, bool open, CancellationToken ct = default);
}

// Eager, Warm opens the model too, so the first line asked about does not wait for it; where
// opening it means downloading it, it opens only once a line is not in the cache.
public sealed class EncoderRanking(IEncoder encoder, IEmbeddingCache? cache = null, bool eager = true) : IRanking
{
    const double Mismatch = 0.1;
    const int MostLines = 4096;

    readonly SemaphoreSlim gate = new(1, 1);
    readonly Wares wares = new(encoder, cache);

    // A line is asked about again with every selection and every mapping that changes the rules.
    readonly Dictionary<string, float[]> lines = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await wares.Index(rs, gewerbe, ct);
            var text = Wares.Normal(line.Name);
            if (!lines.TryGetValue(text, out var query))
            {
                if (lines.Count >= MostLines) lines.Clear();
                lines[text] = query = (await wares.Embed([text], keep: false, ct))[0];
            }
            var best = new Dictionary<string, double>(StringComparer.Ordinal);
            var (owner, ware, rule, vectors) = (wares.Owner, wares.Ware, wares.Rule, wares.Vectors);
            for (var i = 0; i < owner.Length; i++)
            {
                if (!ware[i].ValidOn(date) || rule[i]?.ValidOn(date) == false) continue;
                var cos = Dot(query, vectors, i);
                if (!best.TryGetValue(owner[i], out var b) || cos > b) best[owner[i]] = cos;
            }

            // The encoder reads "Pils" and hardly the keg it comes in; the packaging decides
            // between wares the words cannot tell apart.
            var held = Containers(line);
            foreach (var id in best.Keys)
                if (rs.Products[id].CategoryId is { } c && rs.Categories.GetValueOrDefault(c)?.Contradicts(held) == true)
                    best[id] -= Mismatch;

            await encoder.Load(ct);
            return [.. best
                .OrderByDescending(s => s.Value)
                .ThenBy(s => s.Key, StringComparer.Ordinal)
                .Take(count)
                .Select(s => new Ranked(s.Key, encoder.Confidence(s.Value)))];
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task Warm(RuleSet rs, string gewerbe, bool open, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            await wares.Index(rs, gewerbe, ct);
            if (eager) await encoder.Embed([], ct);
            await encoder.Load(ct);
        }
        finally
        {
            gate.Release();
        }
    }

    // The line's own unit and every container its wording names: "Pils Fass 30 l KEG" is a keg
    // whatever the supplier booked it in.
    static HashSet<string> Containers(InvoiceLine line)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in line.Name.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Append(line.UnitCode))
            if (Units.Lookup(word) is { Container: true } u) held.Add(u.Code);
        return held;
    }

    static readonly char[] Separators = [' ', ',', ';', '/', '(', ')'];

    // Each product in float, summed as doubles.
    static double Dot(float[] query, float[] vectors, int entry)
    {
        var stored = MemoryMarshal.Cast<float, Vector<float>>(vectors.AsSpan(entry * IEncoder.Width, IEncoder.Width));
        var asked = MemoryMarshal.Cast<float, Vector<float>>(query);
        Vector<double> low = default, high = default;
        for (var i = 0; i < stored.Length; i++)
        {
            Vector.Widen(asked[i] * stored[i], out var l, out var h);
            low += l;
            high += h;
        }
        return Vector.Sum(low + high);
    }
}

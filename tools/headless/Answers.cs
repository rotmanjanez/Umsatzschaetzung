using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Headless;

// What the ranking said about one line under one version of the rules.
public sealed record Answer(string Gewerbe, string Name, string Unit, long Version, List<Ranked> Ranked);

// Between a push and a diff every answer of the real ranking is noted, so a browser, which
// runs no model, can give the same ones when a lesson takes the same steps.
public sealed class Notes
{
    readonly Lock gate = new();
    Dictionary<(string, string, string, long), Answer>? noted;

    public void Push()
    {
        lock (gate) noted = [];
    }

    public List<Answer> Diff()
    {
        lock (gate)
        {
            var answers = noted?.Values.ToList() ?? [];
            noted = null;
            return answers;
        }
    }

    public void Note(Answer answer)
    {
        lock (gate)
            if (noted is not null) noted[(answer.Gewerbe, answer.Name, answer.Unit, answer.Version)] = answer;
    }
}

public sealed class Noted(IRanking inner, Notes notes) : IRanking
{
    public async Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default)
    {
        var ranked = await inner.Rank(rs, gewerbe, line, date, count, ct);
        notes.Note(new Answer(gewerbe, line.Name, line.UnitCode, rs.Version, [.. ranked]));
        return ranked;
    }
}

// Gives back what was noted: the answer under the latest rules not newer than those asked
// about, else the earliest. A line never asked about gets no alternatives, as without a model.
public sealed class Replayed(IReadOnlyList<Answer> answers) : IRanking
{
    readonly ILookup<(string, string, string), Answer> byLine = answers.ToLookup(a => (a.Gewerbe, a.Name, a.Unit));

    public Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default)
    {
        var asked = byLine[(gewerbe, line.Name, line.UnitCode)].OrderBy(a => a.Version).ToList();
        var answer = asked.LastOrDefault(a => a.Version <= rs.Version) ?? asked.FirstOrDefault();
        return Task.FromResult<IReadOnlyList<Ranked>>(answer is null ? [] : [.. answer.Ranked.Take(count)]);
    }
}

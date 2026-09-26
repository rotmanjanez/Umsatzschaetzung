using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

sealed class LocalMapping(RuleStore rules, LocalCases cases, IRanking? ranking) : IMapping
{
    const int AutoMapMinConfidence = 80;

    readonly Matcher matcher = new(ranking);

    // A line asked about on its own carries no invoice date; the end of the audit period stands in.
    public Task<List<MappingCandidate>> Suggest(string caseId, InvoiceLine line, string? supplier, CancellationToken ct) => Guard(ct, () =>
    {
        var c = cases.Find(caseId);
        return matcher.Suggest(rules.Load().With(c?.Mappings), c?.Taxpayer.Gewerbe ?? "", supplier, line, c?.PeriodTo ?? Today())
            .Select(sg => new MappingCandidate(sg.Mapping, sg.Confidence, sg.Kind)).ToList();
    });

    // Lines imported before a rule or the model existed, and lines an edit set free,
    // get their turn here: what the matcher is sure about is mapped, the rest stays open.
    // Against the same rules the matcher would answer the same, so a case is asked once per version.
    public Task<Case> Map(string caseId, CancellationToken ct) => Guard(ct, async () =>
    {
        var c = cases.Load(caseId);
        var rs = rules.Load();
        if (c.MappedTo(rs)) return c;
        var before = c.Invoices.SelectMany(i => i.Lines).Select(l => l.MappingId).ToList();
        // One read of the rules serves every invoice: what a line maps on its own is in the set for the next.
        rs = rs.With(c.Mappings);
        foreach (var inv in c.Invoices) rs = (await Lines(rs, inv, c.Taxpayer.Gewerbe, true, c.Mappings, ct)).Rules;
        c.MappedStore = rs.Store;
        c.MappedAt = rs.Version;
        if (!c.Invoices.SelectMany(i => i.Lines).Select(l => l.MappingId).SequenceEqual(before)) cases.Save(c);
        else cases.Store.SaveMappedAt(c.Id, c.MappedStore, c.MappedAt);
        return c;
    });

    // What the matcher decides on its own stays with the case in own; only a person's confirmation
    // puts a mapping into the shared rules.
    public async Task<(RuleSet Rules, List<int> Unmapped)> Lines(RuleSet rs, Invoice inv, string gewerbe, bool ask, Dictionary<string, ArticleMapping> own, CancellationToken ct)
    {
        var unmapped = new List<int>();
        foreach (var l in inv.Lines)
        {
            if (!string.IsNullOrEmpty(l.MappingId)
                && !(rs.Mappings.TryGetValue(l.MappingId, out var m) && Match.Fits(m, inv.SupplierName, inv.Date ?? Today(), l)))
                l.MappingId = null;
            if (string.IsNullOrEmpty(l.MappingId)) rs = await Line(rs, gewerbe, inv, l, ask, own, ct);
            if (string.IsNullOrEmpty(l.MappingId)) unmapped.Add((int)l.No);
        }
        return (rs, unmapped);
    }

    async Task<RuleSet> Line(RuleSet rs, string gewerbe, Invoice inv, InvoiceLine l, bool ask, Dictionary<string, ArticleMapping> own, CancellationToken ct)
    {
        if (Match.Mapping(rs, inv.SupplierName, inv.Date ?? Today(), l) is { } hit)
        {
            l.MappingId = hit.Id;
            return rs;
        }
        if (!ask) return rs;
        var sugs = await Task.Run(() => matcher.Suggest(rs, gewerbe, inv.SupplierName, l, inv.Date ?? Today()), ct);
        if (sugs.Count == 0) return rs;
        var sg = sugs[0];
        if (sg.Kind == OriginKind.Exact)
        {
            l.MappingId = sg.Mapping.Id;
            return rs;
        }
        if (sg.Confidence < AutoMapMinConfidence) return rs;
        var m = sg.Mapping;
        m.Id = Ids.New();
        var next = rs.With(new Dictionary<string, ArticleMapping> { [m.Id] = m });
        try
        {
            RuleCheck.Validate(next, m);
        }
        catch (RulesException)
        {
            return rs;
        }
        own[m.Id] = m;
        l.MappingId = m.Id;
        return next;
    }
}

using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;
using Umsatzschaetzung.Rulestore;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

sealed class LocalRules(RuleStore rules, string appVersion) : IRules
{
    public Task<StatusResp> Status(CancellationToken ct) => Guard(ct, () =>
    {
        var rs = rules.Load();
        return new StatusResp(rs.Version, RulesDate(rs), appVersion, rules.Notice);
    });

    static DateTimeOffset RulesDate(RuleSet rs) =>
        rs.Categories.Values.Select(e => e.Meta.ChangedAt)
            .Concat(rs.Mappings.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Products.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.YieldRules.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Gewerbezweige.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Templates.Values.Select(e => e.Meta.ChangedAt))
            .DefaultIfEmpty(default)
            .Max();

    public Task<RuleSet> Load(CancellationToken ct) => Guard(ct, rules.Load);

    public Task<RuleSet> Save(IRuleEntity rule, CancellationToken ct) => Change(new([RuleArg.Of(rule)], []), ct);

    public Task<RuleSet> Delete(Entity kind, string id, CancellationToken ct) => Change(new([], [new(kind, id)]), ct);

    // The set is checked as the change leaves it, so entries that only fit together go in together.
    public Task<RuleSet> Change(RulesChange change, CancellationToken ct) => Guard(ct, () =>
    {
        var before = rules.Load();
        List<DeleteRuleArg> delete = change.Delete ?? [];
        var put = (change.Put ?? []).Select(a => a.Rule).OrderBy(r => r is ReportTemplate { Default: true } ? 0 : 1).ToList();
        if (put.Count == 0 && delete.Count == 0) return before;
        if (put.Find(p => delete.Exists(d => d.Kind == RuleSet.KindOf(p) && d.Id == p.Id)) is { } both)
            throw new ServiceError(ErrorCode.Invalid, $"{Format.EntityName(RuleSet.KindOf(both))} „{both.Id}“ soll zugleich gespeichert und gelöscht werden");
        var rs = Json.Copy(before);
        foreach (var rule in put)
        {
            if (rule is ReportTemplate { Default: false } && rs.Find(Entity.Template, rule.Id) is ReportTemplate { Default: true })
                throw new ServiceError(ErrorCode.Conflict, "Eine Vorlage bleibt Standard, bis eine andere zum Standard wird");
            if (rule is ReportTemplate { Default: true })
                foreach (var t in rs.Templates.Values) t.Default = false;
            if (rule is ArticleMapping { Confirmed: false })
                throw new ServiceError(ErrorCode.Invalid, "In die Regeln kommt nur eine bestätigte Zuordnung");
            rs.Put(rule);
        }
        foreach (var (kind, id) in delete)
        {
            if (rs.Find(kind, id) is null) throw new ServiceError(ErrorCode.NotFound, $"{Format.EntityName(kind)} „{id}“");
            if (rs.Find(kind, id) is ReportTemplate { Default: true })
                throw new ServiceError(ErrorCode.Conflict, "Die Standardvorlage lässt sich nicht löschen; erst eine andere zum Standard machen");
            rs.Remove(kind, id);
        }
        foreach (var (kind, id) in delete)
            if (RuleCheck.Users(rs, kind, id) is { Count: > 0 } users)
                throw new ServiceError(ErrorCode.Conflict, $"{Format.EntityName(kind)} wird noch verwendet von: {string.Join(", ", users)}");
        RuleCheck.Validate(rs);
        RuleCheck.Rescaled(before, rs, put);
        return rules.Change(put, [.. delete.Select(d => (d.Kind, d.Id))]);
    });
}

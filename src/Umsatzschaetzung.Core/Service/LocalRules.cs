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
            .Concat(rs.Ingredients.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Mappings.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Products.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.YieldRules.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Gewerbezweige.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Templates.Values.Select(e => e.Meta.ChangedAt))
            .DefaultIfEmpty(default)
            .Max();

    public Task<RuleSet> Load(CancellationToken ct) => Guard(ct, () => Json.Copy(rules.Load()));

    public Task<RuleSet> Save(IRuleEntity rule, CancellationToken ct) => Guard(ct, () =>
    {
        var rs = Json.Copy(rules.Load());
        if (rule is ReportTemplate { Default: false } && rs.Find(Entity.Template, rule.Id) is ReportTemplate { Default: true })
            throw new ServiceError(ErrorCode.Conflict, "Eine Vorlage bleibt Standard, bis eine andere zum Standard wird");
        if (rule is ReportTemplate { Default: true })
            foreach (var t in rs.Templates.Values) t.Default = false;
        if (rule is ArticleMapping { Confirmed: false })
            throw new ServiceError(ErrorCode.Invalid, "In die Regeln kommt nur eine bestätigte Zuordnung");
        rs.Put(rule);
        RuleCheck.Validate(rs);
        return rules.Save(rule);
    });

    public Task<RuleSet> Delete(Entity kind, string id, CancellationToken ct) => Guard(ct, () =>
    {
        var rs = rules.Load();
        if (rs.Find(kind, id) is null) throw new ServiceError(ErrorCode.NotFound, $"{Format.EntityName(kind)} „{id}“");
        if (rs.Find(kind, id) is ReportTemplate { Default: true })
            throw new ServiceError(ErrorCode.Conflict, "Die Standardvorlage lässt sich nicht löschen; erst eine andere zum Standard machen");
        if (RuleCheck.Users(rs, kind, id) is { Count: > 0 } users)
            throw new ServiceError(ErrorCode.Conflict, $"{Format.EntityName(kind)} wird noch verwendet von: {string.Join(", ", users)}");
        return rules.Delete(kind, id);
    });
}

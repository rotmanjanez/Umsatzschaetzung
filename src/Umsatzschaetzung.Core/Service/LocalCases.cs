using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

// The cases, and how the other local parts load and save one.
sealed class LocalCases(RuleStore rules, CaseStore cases) : ICases
{
    public CaseStore Store => cases;

    public Task<CasesResp> List(CancellationToken ct) => Guard(ct, () =>
    {
        var (list, unreadable) = cases.List();
        return new CasesResp(list, unreadable);
    });

    public Task<Case> Get(string caseId, CancellationToken ct) => Guard(ct, () => Load(caseId));

    public Task<Case> Put(Case kase, CancellationToken ct) => Guard(ct, () =>
    {
        if (kase.Products?.Exists(p => p.Recipe is not null) == true) KnownIngredients(kase, rules.Load());
        Save(kase);
        return kase;
    });

    static void KnownIngredients(Case c, RuleSet rs)
    {
        foreach (var p in c.Products)
            foreach (var l in p.Recipe ?? [])
                if (!rs.Ingredients.ContainsKey(l.IngredientId))
                    throw new ServiceError(ErrorCode.Invalid, $"Produkt \"{p.ProductId}\": Zutat \"{l.IngredientId}\" existiert nicht");
    }

    public Task Delete(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        if (caseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        cases.Delete(caseId);
        return Task.FromResult(0);
    });

    public Task<Case> Import(string fileName, byte[] data, bool overwrite, CancellationToken ct) =>
        Guard(ct, () => cases.Import(data, overwrite));

    public Task<ExportResp> Export(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = Load(caseId);
        return new ExportResp(cases.Export(caseId), FileName(c.Label, "db"));
    });

    public Case Load(string id)
    {
        if (id == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        return cases.Load(id);
    }

    public Case? Find(string caseId)
    {
        if (caseId == "") return null;
        try { return cases.Load(caseId); }
        catch (CaseNotFoundException) { return null; }
    }

    public void Save(Case c, Attachment? add = null)
    {
        var t = Clock.Now();
        if (c.Id == "") c.Id = Ids.New();
        if (c.CreatedAt == default) c.CreatedAt = t;
        c.UpdatedAt = t;
        cases.Save(c, add);
    }
}

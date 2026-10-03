using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

// The cases, and how the other local parts load and save one.
sealed class LocalCases(RuleStore rules, CaseStore cases) : ICases
{
    public CaseStore Store => cases;

    public Excerpts Excerpts { get; } = new(cases);

    public Task<CasesResp> List(CancellationToken ct) => Guard(ct, () =>
    {
        var (list, unreadable) = cases.List();
        return new CasesResp(list, unreadable);
    });

    public Task<Case> Get(string caseId, CancellationToken ct) => Guard(ct, () => Load(caseId));

    public Task<Case> Put(Case kase, CancellationToken ct) => Guard(ct, () =>
    {
        if (kase.Products?.Exists(p => p.Recipe is not null) == true) KnownParts(kase, rules.Load());
        Save(kase);
        return kase;
    });

    static void KnownParts(Case c, RuleSet rs)
    {
        foreach (var p in c.Products)
            foreach (var l in p.Recipe ?? [])
            {
                if (!rs.Products.TryGetValue(l.PartId, out var part))
                    throw new ServiceError(ErrorCode.Invalid, $"Produkt \"{p.ProductId}\": Bestandteil \"{l.PartId}\" existiert nicht");
                if (Units.Lookup(l.Unit) is not { Container: false } u)
                    throw new ServiceError(ErrorCode.Invalid, $"Produkt \"{p.ProductId}\": \"{part.Name}\" hat die unbekannte Einheit \"{l.Unit}\"");
                if (Scale.Of(part) is { } partUnit && u.Base != partUnit)
                    throw new ServiceError(ErrorCode.Invalid, $"Produkt \"{p.ProductId}\": \"{part.Name}\" zählt in {Format.UnitName(partUnit)}, nicht in {u.Name}");
            }
    }

    public Task Delete(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        if (caseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        cases.Delete(caseId);
        Excerpts.Forget(caseId);
        return Task.FromResult(0);
    });

    public Task<Case> Import(string fileName, byte[] data, bool overwrite, CancellationToken ct) =>
        Guard(ct, () =>
        {
            var c = cases.Import(data, overwrite);
            Excerpts.Forget(c.Id);
            return c;
        });

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
        if (add is not null) Excerpts.Forget(c.Id, add.InvoiceId);
    }

    public void Amend(Case was, Case now) => cases.Amend(was, now, Clock.Now());

    public string? SaveInvoice(string caseId, Invoice inv, Dictionary<string, ArticleMapping> mappings, Attachment add)
    {
        if (caseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        var store = cases.SaveInvoice(caseId, inv, mappings, add, Clock.Now());
        Excerpts.Forget(caseId, add.InvoiceId);
        return store;
    }
}

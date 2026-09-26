using System.Text;
using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Invoices;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Rules;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Service;

public sealed class LocalService(RuleStore rules, CaseStore cases, IDocuments? documents, ITagger? tagger, IRanking? ranking, IPdfPrinter? printer, string appVersion, Readings? readings = null) : IService
{
    const int AutoMapMinConfidence = 80;

    readonly Matcher matcher = new(ranking);
    readonly Excerpts excerpts = new(cases);

    IDocuments Documents => documents ?? throw new ServiceError(ErrorCode.Unsupported, "Belege lassen sich hier nicht lesen");

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

    public Task<RuleSet> Rules(CancellationToken ct) => Guard(ct, rules.Load);

    public Task<RuleSet> SaveRule(IRuleEntity rule, CancellationToken ct) => Guard(ct, () =>
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

    public Task<RuleSet> DeleteRule(Entity kind, string id, CancellationToken ct) => Guard(ct, () =>
    {
        var rs = rules.Load();
        if (rs.Find(kind, id) is null) throw new ServiceError(ErrorCode.NotFound, $"{Format.EntityName(kind)} \u201e{id}\u201c");
        if (rs.Find(kind, id) is ReportTemplate { Default: true })
            throw new ServiceError(ErrorCode.Conflict, "Die Standardvorlage lässt sich nicht löschen; erst eine andere zum Standard machen");
        if (RuleCheck.Users(rs, kind, id) is { Count: > 0 } users)
            throw new ServiceError(ErrorCode.Conflict, $"{Format.EntityName(kind)} wird noch verwendet von: {string.Join(", ", users)}");
        return rules.Delete(kind, id);
    });

    public Task<List<SammlungInfo>> Sammlungen(CancellationToken ct) => Guard(ct, rules.Sammlungen);

    public Task<List<SammlungInfo>> ImportSammlung(string fileName, byte[] pdf, CancellationToken ct) => Guard(ct, () =>
    {
        if (pdf.Length == 0) throw new ServiceError(ErrorCode.Invalid, "Leere Datei");
        return rules.ImportSammlung(Richtsätze.Read(Documents.Sheets(pdf)), fileName);
    });

    public Task<List<SammlungInfo>> DeleteSammlung(int year, CancellationToken ct) => Guard(ct, () =>
    {
        if (!rules.Sammlungen().Exists(s => s.Year == year)) throw new ServiceError(ErrorCode.NotFound, $"Richtsatzsammlung {year}");
        return rules.DeleteSammlung(year);
    });

    public Task<CasesResp> ListCases(CancellationToken ct) => Guard(ct, () =>
    {
        var (list, unreadable) = cases.List();
        return new CasesResp(list, unreadable);
    });

    public Task<Case> GetCase(string caseId, CancellationToken ct) => Guard(ct, () => LoadCase(caseId));

    public Task<Case> PutCase(Case kase, CancellationToken ct) => Guard(ct, () =>
    {
        if (kase.Products?.Exists(p => p.Recipe is not null) == true) KnownIngredients(kase, rules.Load());
        SaveCase(kase);
        return kase;
    });

    static void KnownIngredients(Case c, RuleSet rs)
    {
        foreach (var p in c.Products)
            foreach (var l in p.Recipe ?? [])
                if (!rs.Ingredients.ContainsKey(l.IngredientId))
                    throw new ServiceError(ErrorCode.Invalid, $"Produkt \"{p.ProductId}\": Zutat \"{l.IngredientId}\" existiert nicht");
    }

    public Task DeleteCase(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        if (caseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        cases.Delete(caseId);
        excerpts.Forget(caseId);
        return Task.FromResult(0);
    });

    public Task<Case> ImportCase(string fileName, byte[] data, bool overwrite, CancellationToken ct) =>
        Guard(ct, () =>
        {
            var c = cases.Import(data, overwrite);
            excerpts.Forget(c.Id);
            return c;
        });

    public Task<ExportResp> ExportCase(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = LoadCase(caseId);
        return new ExportResp(cases.Export(caseId), FileName(c.Label, "db"));
    });

    public Task<ExportResp> ExportInvoice(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = LoadCase(caseId);
        var inv = c.Invoices.Find(i => i.Id == invoiceId)
            ?? throw new ServiceError(ErrorCode.NotFound, $"Rechnung \"{invoiceId}\" nicht gefunden");
        var label = inv.Number != "" ? inv.Number : inv.FileName;
        return new ExportResp(Csv.Invoice(c, inv, rules.Load().With(c.Mappings)), FileName(c.Label + " " + label, "csv"));
    });

    public Task<ExportResp> ExportAssortment(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = LoadCase(caseId);
        return new ExportResp(Csv.Assortment(c, rules.Load()), FileName(c.Label + " Sortiment", "csv"));
    });

    public Task<AssortmentImport> ReadAssortment(byte[] data, CancellationToken ct) => Guard(ct, () => Csv.ReadAssortment(data, rules.Load()));

    public Task<ParseResp> ParseInvoice(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(ct, async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        switch (InvoiceParser.Detect(data))
        {
            case Kind.Pdf or Kind.Image:
                return new ParseResp(new Invoice(), [], true, null);
            case Kind.Unknown:
                throw new ServiceError(ErrorCode.Unsupported, $"Dateiformat von \"{fileName}\" nicht erkannt");
        }
        var kase = caseId == "" ? null : LoadCase(caseId);
        var own = kase?.Mappings ?? [];
        var inv = InvoiceParser.Parse(fileName, data);
        inv.Id = Ids.New();
        var (_, unmapped) = await MapLines(rules.Load().With(own), inv, kase?.Taxpayer.Gewerbe ?? "", true, own, ct);
        var c = kase is null ? null : Attach(caseId, inv, fileName, data, own);
        return new ParseResp(inv, unmapped, false, c);
    });

    public Task<OcrResp> OcrInvoice(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(ct, async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        var (pages, draft) = await Read(fileName, data, ct);
        draft.Id = Ids.New();
        draft.FileName = fileName;
        (draft.NetTotal, draft.GrossTotal) = InvoiceMath.LineTotals(draft.Lines);
        var kase = Find(caseId);
        await MapLines(rules.Load().With(kase?.Mappings), draft, kase?.Taxpayer.Gewerbe ?? "", false, kase?.Mappings ?? [], ct);
        return new OcrResp(draft.Id, pages, draft);
    });

    async Task<(List<OcrPage>, Invoice)> Read(string fileName, byte[] data, CancellationToken ct)
    {
        if (readings?.Find(data) is { } known) return (known.Pages, known.Draft);
        if (documents is null || tagger is null) throw new ServiceError(ErrorCode.Unsupported, "Texterkennung nicht verfügbar");
        var pages = await documents.Read(fileName, data, ct);
        var draft = await Extractor.InvoiceAsync(tagger, pages, ct, (at, regions, ct) => documents.Reread(data, pages[at], at, regions, ct));
        readings?.Keep(data, new OcrResp("", pages, draft));
        return (pages, draft);
    }

    public Task<VerifyResp> VerifyInvoice(VerifyReq req, CancellationToken ct) => Guard(ct, async () =>
    {
        var inv = req.Invoice;
        if (inv.Id == "") inv.Id = Ids.New();
        if (inv.Source == Source.Scan)
            (inv.NetTotal, inv.GrossTotal) = InvoiceMath.LineTotals(inv.Lines);
        var flags = Check.Invoice(inv);
        var blocked = flags.Any(f => Check.Blocks(inv, f));
        var confirm = req.Intent switch
        {
            Intent.Confirm => !blocked,
            Intent.Auto => Check.Complete(inv, flags),
            _ => false,
        };
        var store = confirm || req.Intent is Intent.Store or Intent.Auto;
        var kase = store ? LoadCase(req.CaseId) : Find(req.CaseId);
        var own = kase?.Mappings ?? [];
        await MapLines(rules.Load().With(own), inv, kase?.Taxpayer.Gewerbe ?? "", confirm, own, ct);
        var resp = new VerifyResp(inv, flags, blocked, false, null);
        if (!store) return resp;
        inv.Verification = confirm ? new Verification { At = Clock.Now(), Auto = req.Intent == Intent.Auto } : null;
        var c = Attach(req.CaseId, inv, req.FileName ?? inv.FileName, req.Data ?? [], own, req.Reading);
        return resp with { Invoice = inv, Case = c, Accepted = confirm };
    });

    public Task<Case> DeleteInvoice(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, () =>
    {
        var c = LoadCase(caseId);
        var i = c.Invoices.FindIndex(x => x.Id == invoiceId);
        if (i < 0) throw new ServiceError(ErrorCode.NotFound, $"Rechnung \"{invoiceId}\" nicht im Fall \"{caseId}\"");
        c.Invoices.RemoveAt(i);
        SaveCase(c);
        return c;
    });

    public Task<InvoiceSourceResp> InvoiceSource(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, async () =>
    {
        string name;
        byte[] data;
        try
        {
            (name, data) = cases.LoadFile(caseId, invoiceId);
        }
        catch (CaseNotFoundException e)
        {
            throw new ServiceError(ErrorCode.NotFound, $"kein Beleg zu Rechnung {invoiceId} gespeichert", inner: e);
        }
        if (InvoiceParser.Detect(data) is not (Kind.Image or Kind.Pdf or Kind.Zugferd))
            return new InvoiceSourceResp(name, [new SourcePage(null, Encoding.UTF8.GetString(data))]);
        var read = cases.LoadReading(caseId, invoiceId)?.Select(p => p.Correction).ToList() ?? [];
        var pages = new List<SourcePage>();
        await foreach (var image in Documents.Preview(data, read, ct))
            pages.Add(new SourcePage(image, null));
        return new InvoiceSourceResp(name, pages);
    });

    public Task<InvoiceReadingResp> InvoiceReading(string caseId, string invoiceId, CancellationToken ct) => Guard(ct, async () =>
    {
        if (cases.LoadReading(caseId, invoiceId) is not { } pages) return new InvoiceReadingResp([]);
        byte[] data;
        try
        {
            (_, data) = cases.LoadFile(caseId, invoiceId);
        }
        catch (CaseNotFoundException)
        {
            return new InvoiceReadingResp([]);
        }
        if (documents is not null && InvoiceParser.Detect(data) is Kind.Image or Kind.Pdf)
        {
            var i = 0;
            await foreach (var image in documents.Pages(data, [.. pages.Select(p => p.Correction)], ct))
                pages[i++].Image = image;
        }
        return new InvoiceReadingResp(pages);
    });

    public Task<Raster?> InvoiceSnippet(string caseId, string invoiceId, int line, string name, CancellationToken ct) =>
        Guard(ct, () => excerpts.Row(Documents, caseId, invoiceId, line, name));

    // A line asked about on its own carries no invoice date; the end of the audit period stands in.
    public Task<List<MappingCandidate>> SuggestMapping(string caseId, InvoiceLine line, string? supplier, CancellationToken ct) => Guard(ct, () =>
    {
        var c = Find(caseId);
        return matcher.Suggest(rules.Load().With(c?.Mappings), c?.Taxpayer.Gewerbe ?? "", supplier, line, c?.PeriodTo ?? Today())
            .Select(sg => new MappingCandidate(Json.Copy(sg.Mapping), sg.Confidence, sg.Kind)).ToList();
    });

    // Lines imported before a rule or the model existed, and lines an edit set free,
    // get their turn here: what the matcher is sure about is mapped, the rest stays open.
    // Against the same rules the matcher would answer the same, so a case is asked once per version.
    public Task<Case> MapCase(string caseId, CancellationToken ct) => Guard(ct, async () =>
    {
        var c = LoadCase(caseId);
        var rs = rules.Load();
        if (c.MappedTo(rs)) return c;
        var before = c.Invoices.SelectMany(i => i.Lines).Select(l => l.MappingId).ToList();
        // One read of the rules serves every invoice: what a line maps on its own is in the set for the next.
        rs = rs.With(c.Mappings);
        foreach (var inv in c.Invoices) rs = (await MapLines(rs, inv, c.Taxpayer.Gewerbe, true, c.Mappings, ct)).Rules;
        c.MappedStore = rs.Store;
        c.MappedAt = rs.Version;
        if (!c.Invoices.SelectMany(i => i.Lines).Select(l => l.MappingId).SequenceEqual(before)) SaveCase(c);
        else cases.SaveMappedAt(c.Id, c.MappedStore, c.MappedAt);
        return c;
    });

    // A line mapped by article number under the old name is asked again under the new one.
    public Task<Case> UnifySuppliers(string caseId, List<string> invoiceIds, CancellationToken ct) => Guard(ct, () =>
    {
        var c = LoadCase(caseId);
        if (!Suppliers.Unify(c, invoiceIds.ToHashSet())) return c;
        c.MappedStore = null;
        SaveCase(c);
        return c;
    });

    public Task<CalcResp> Calculate(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        var (rep, _, rahmen, _) = Compute(LoadCase(caseId));
        return new CalcResp(rep, rahmen);
    });

    public Task<ReportResp> RenderReport(string caseId, bool pdf, CancellationToken ct) => Guard(ct, async () =>
    {
        var c = LoadCase(caseId);
        var (rep, rs, rahmen, sammlung) = Compute(c);
        var html = Html.Render(c, rs, rep, rahmen, sammlung?.Sammlung, sammlung?.Info, appVersion);
        if (!pdf) return new ReportResp(html, null, null);
        if (printer is null) throw new ServiceError(ErrorCode.Unsupported, "PDF-Ausgabe nicht verfügbar");
        return new ReportResp(html, Documents.Stamp(await printer.Print(html, ct), Html.Marks(c, rep)), FileName(c.Label, "pdf"));
    });

    (Model.Report Report, RuleSet Rules, Rahmen? Rahmen, (Sammlung Sammlung, SammlungInfo Info)? Sammlung) Compute(Case c)
    {
        var rs = rules.Load();
        Model.Report rep;
        try
        {
            rep = Calculation.Run(c, rs);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            throw new ServiceError(ErrorCode.Invalid, "Kalkulation: " + e.Message, inner: e);
        }
        var sammlung = Sammlung(c.PeriodTo.Year);
        var rahmen = Vergleich.Aufschlag(sammlung?.Sammlung, c.Taxpayer.Gewerbe, rep.Totals.RevenueNet);
        // Verglichen wird der Satz des Betriebs, nicht der einer Sparte: die Sammlung staffelt
        // den Aufschlag nach Gewerbeklasse, nicht nach Getränken und Speisen.
        if (rahmen is not null && rahmen.Lage(rep.Totals.Markup) is var lage && lage != Rahmenlage.Im)
            rep.Warnings.Add(new Flag
            {
                Code = "markup-out-of-range",
                Message = $"Rohgewinnaufschlag {Format.Bp(rep.Totals.Markup)} liegt {(lage == Rahmenlage.Unter ? "unter" : "über")} dem Rahmensatz "
                    + $"{rahmen.Von} bis {rahmen.Bis} v.H. der Richtsatzsammlung {rahmen.Jahr} für „{rahmen.Klasse}“",
            });
        return (rep, rs, rahmen, sammlung);
    }

    // Die Sammlung des Prüfungsjahres, sonst die jüngste davor.
    (Sammlung Sammlung, SammlungInfo Info)? Sammlung(int year)
    {
        SammlungInfo? found = null;
        foreach (var i in rules.Sammlungen())
            if (i.Year <= year && i.Year > (found?.Year ?? -1)) found = i;
        return found is not null && rules.Sammlung(found.Year) is { } s ? (s, found) : null;
    }

    Case LoadCase(string id)
    {
        if (id == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        return cases.Load(id);
    }

    void SaveCase(Case c, Attachment? add = null)
    {
        var t = Clock.Now();
        if (c.Id == "") c.Id = Ids.New();
        if (c.CreatedAt == default) c.CreatedAt = t;
        c.UpdatedAt = t;
        cases.Save(c, add);
        if (add is not null) excerpts.Forget(c.Id, add.InvoiceId);
    }

    Case Attach(string caseId, Invoice inv, string fileName, byte[] data, Dictionary<string, ArticleMapping> own, List<OcrPage>? reading = null)
    {
        var c = LoadCase(caseId);
        foreach (var (id, m) in own) c.Mappings[id] = m;
        var i = c.Invoices.FindIndex(x => x.Id == inv.Id);
        if (i >= 0) c.Invoices[i] = inv;
        else c.Invoices.Add(inv);
        if (inv.Lines.Any(l => string.IsNullOrEmpty(l.MappingId))) c.MappedStore = null;
        SaveCase(c, new Attachment(inv.Id, fileName, data, reading));
        return c;
    }

    Case? Find(string caseId)
    {
        if (caseId == "") return null;
        try { return cases.Load(caseId); }
        catch (CaseNotFoundException) { return null; }
    }

    // What the matcher decides on its own stays with the case in own; only a person's confirmation
    // puts a mapping into the shared rules.
    async Task<(RuleSet Rules, List<int> Unmapped)> MapLines(RuleSet rs, Invoice inv, string gewerbe, bool ask, Dictionary<string, ArticleMapping> own, CancellationToken ct)
    {
        var unmapped = new List<int>();
        foreach (var l in inv.Lines)
        {
            if (!string.IsNullOrEmpty(l.MappingId)
                && !(rs.Mappings.TryGetValue(l.MappingId, out var m) && Match.Fits(m, inv.SupplierName, inv.Date ?? Today(), l)))
                l.MappingId = null;
            if (string.IsNullOrEmpty(l.MappingId)) rs = await MapLine(rs, gewerbe, inv, l, ask, own, ct);
            if (string.IsNullOrEmpty(l.MappingId)) unmapped.Add((int)l.No);
        }
        return (rs, unmapped);
    }

    async Task<RuleSet> MapLine(RuleSet rs, string gewerbe, Invoice inv, InvoiceLine l, bool ask, Dictionary<string, ArticleMapping> own, CancellationToken ct)
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

    static string FileName(string label, string ext)
    {
        var b = new StringBuilder();
        foreach (var r in label)
        {
            switch (r)
            {
                case >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_': b.Append(r); break;
                case 'ä': b.Append('a'); break;
                case 'ö': b.Append('o'); break;
                case 'ü': b.Append('u'); break;
                case 'Ä': b.Append('A'); break;
                case 'Ö': b.Append('O'); break;
                case 'Ü': b.Append('U'); break;
                case 'ß': b.Append('s'); break;
                case ' ': b.Append('_'); break;
            }
        }
        var name = b.Length == 0 ? "Prüfung" : b.ToString();
        return "Umsatzschätzung-" + name + "-" + Today().ToString("yyyy-MM-dd") + "." + ext;
    }

    static DateOnly Today() => DateOnly.FromDateTime(DateTime.Now);

    static Task<T> Guard<T>(CancellationToken ct, Func<T> body) => Guard(ct, () => Task.FromResult(body()));

    static async Task<T> Guard<T>(CancellationToken ct, Func<Task<T>> body)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            return await Task.Run(body, ct);
        }
        catch (CaseExistsException e)
        {
            throw new ServiceError(ErrorCode.Conflict, e.Message, e.Labels, e);
        }
        catch (Exception e) when (e is not (ServiceError or OperationCanceledException))
        {
            throw new ServiceError(e switch
            {
                CaseNotFoundException => ErrorCode.NotFound,
                CaseInvalidException or RulesException or InvalidDataException => ErrorCode.Invalid,
                StoreUnavailableException => ErrorCode.Unavailable,
                _ => ErrorCode.Internal,
            }, e.Message, inner: e);
        }
    }
}

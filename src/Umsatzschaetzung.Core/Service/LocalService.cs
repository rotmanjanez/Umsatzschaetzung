using System.Security.Cryptography;
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

public sealed class LocalService(RuleStore rules, CaseStore cases, IOcr? ocr, Tagger tagger, IPdfPages? pdf, IPdfPrinter? printer, string appVersion) : IService
{
    const int AutoMapMinConfidence = 80;
    const int PreviewDpi = 150;

    readonly Matcher matcher = new(new EmbeddingStore(rules.Dir));

    public Task<StatusResp> Status(CancellationToken ct) => Guard(() =>
    {
        var rs = rules.Load();
        return new StatusResp(rs.Version, RulesDate(rs), appVersion, rules.Notice);
    });

    static DateTimeOffset RulesDate(RuleSet rs) =>
        rs.Ingredients.Values.Select(e => e.Meta.ChangedAt)
            .Concat(rs.Mappings.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.Products.Values.Select(e => e.Meta.ChangedAt))
            .Concat(rs.YieldRules.Values.Select(e => e.Meta.ChangedAt))
            .DefaultIfEmpty(default)
            .Max();

    public Task<RuleSet> Rules(CancellationToken ct) => Guard(rules.Load);

    public Task<RuleSet> SaveRule(IRuleEntity rule, CancellationToken ct) => Guard(() =>
    {
        var rs = rules.Load();
        rs.Put(rule);
        RuleCheck.Validate(rs);
        return rules.Save(rule);
    });

    public Task<RuleSet> DeleteRule(Entity kind, string id, CancellationToken ct) => Guard(() =>
    {
        var rs = rules.Load();
        if (rs.Find(kind, id) is null) throw new ServiceError(ErrorCode.NotFound, $"{Format.EntityName(kind)} \u201e{id}\u201c");
        if (RuleCheck.Users(rs, kind, id) is { Count: > 0 } users)
            throw new ServiceError(ErrorCode.Conflict, $"{Format.EntityName(kind)} wird noch verwendet von: {string.Join(", ", users)}");
        return rules.Delete(kind, id);
    });

    public Task<List<SammlungInfo>> Sammlungen(CancellationToken ct) => Guard(rules.Sammlungen);

    public Task<List<SammlungInfo>> ImportSammlung(string fileName, byte[] pdf, CancellationToken ct) => Guard(() =>
    {
        if (pdf.Length == 0) throw new ServiceError(ErrorCode.Invalid, "Leere Datei");
        return rules.ImportSammlung(Richtsätze.Read(pdf), fileName);
    });

    public Task<List<SammlungInfo>> DeleteSammlung(int year, CancellationToken ct) => Guard(() =>
    {
        if (!rules.Sammlungen().Exists(s => s.Year == year)) throw new ServiceError(ErrorCode.NotFound, $"Richtsatzsammlung {year}");
        return rules.DeleteSammlung(year);
    });

    public Task<List<Case>> ListCases(CancellationToken ct) => Guard(cases.List);

    public Task<Case> GetCase(string caseId, CancellationToken ct) => Guard(() => LoadCase(caseId));

    public Task<Case> PutCase(Case kase, CancellationToken ct) => Guard(() =>
    {
        SaveCase(kase);
        return kase;
    });

    public Task DeleteCase(string caseId, CancellationToken ct) => Guard(() =>
    {
        if (caseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        cases.Delete(caseId);
        return Task.FromResult(0);
    });

    public Task<Case> ImportCase(string fileName, byte[] data, bool overwrite, CancellationToken ct) => Guard(() =>
    {
        try
        {
            return cases.Import(data, overwrite);
        }
        catch (CaseExistsException e)
        {
            throw new ServiceError(ErrorCode.Conflict, e.Message, e.Label, e);
        }
    });

    public Task<ExportResp> ExportCase(string caseId, CancellationToken ct) => Guard(() =>
    {
        var c = LoadCase(caseId);
        return new ExportResp(cases.Export(caseId), FileName(c.Label, "db"));
    });

    public Task<ExportResp> ExportInvoice(string caseId, string invoiceId, CancellationToken ct) => Guard(() =>
    {
        var c = LoadCase(caseId);
        var inv = c.Invoices.Find(i => i.Id == invoiceId)
            ?? throw new ServiceError(ErrorCode.NotFound, $"Rechnung \"{invoiceId}\" nicht gefunden");
        var label = inv.Number != "" ? inv.Number : inv.FileName;
        return new ExportResp(Csv.Invoice(c, inv, rules.Load()), FileName(c.Label + " " + label, "csv"));
    });

    public Task<ParseResp> ParseInvoice(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        switch (InvoiceParser.Detect(data))
        {
            case Kind.Pdf or Kind.Image:
                return new ParseResp(new Invoice(), [], true, null);
            case Kind.Unknown:
                throw new ServiceError(ErrorCode.Unsupported, $"Dateiformat von \"{fileName}\" nicht erkannt");
        }
        var inv = InvoiceParser.Parse(fileName, data);
        inv.Id = NewId("re-");
        var (_, unmapped) = await MapLines(inv, Gewerbe(caseId), true, ct);
        var c = caseId == "" ? null : Attach(caseId, inv, fileName, data);
        return new ParseResp(inv, unmapped, false, c);
    });

    public Task<OcrResp> OcrInvoice(string caseId, string fileName, byte[] data, CancellationToken ct) => Guard(async () =>
    {
        if (data.Length == 0) throw new ServiceError(ErrorCode.Invalid, $"leere Datei \"{fileName}\"");
        var pages = await Scan.Read(ocr, pdf, fileName, data, Scan.Dpi, ct);
        var draft = await Extractor.InvoiceAsync(tagger, pages, ct);
        draft.Id = NewId("re-");
        draft.FileName = fileName;
        (draft.NetTotal, draft.GrossTotal) = InvoiceMath.LineTotals(draft.Lines);
        await MapLines(draft, Gewerbe(caseId), false, ct);
        return new OcrResp(draft.Id, pages, draft);
    });

    public Task<VerifyResp> VerifyInvoice(VerifyReq req, CancellationToken ct) => Guard(async () =>
    {
        var inv = req.Invoice;
        if (inv.Id == "") inv.Id = NewId("re-");
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
        if (store && req.CaseId == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        await MapLines(inv, Gewerbe(req.CaseId), confirm, ct);
        var resp = new VerifyResp(inv, flags, blocked, false, null);
        if (!store) return resp;
        if (confirm) inv.Verification = new Verification { At = Clock.Now(), Auto = req.Intent == Intent.Auto };
        var c = Attach(req.CaseId, inv, req.FileName ?? inv.FileName, req.Data ?? [], req.Reading);
        return resp with { Invoice = inv, Case = c, Accepted = confirm };
    });

    public Task<Case> DeleteInvoice(string caseId, string invoiceId, CancellationToken ct) => Guard(() =>
    {
        var c = LoadCase(caseId);
        var i = c.Invoices.FindIndex(x => x.Id == invoiceId);
        if (i < 0) throw new ServiceError(ErrorCode.NotFound, $"Rechnung \"{invoiceId}\" nicht im Fall \"{caseId}\"");
        c.Invoices.RemoveAt(i);
        cases.DeleteFile(caseId, invoiceId);
        SaveCase(c);
        return c;
    });

    public Task<InvoiceSourceResp> InvoiceSource(string caseId, string invoiceId, CancellationToken ct) => Guard(async () =>
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
        var pages = InvoiceParser.Detect(data) switch
        {
            Kind.Image => [new SourcePage(data, null)],
            Kind.Pdf or Kind.Zugferd => (await Scan.Render(pdf, data, PreviewDpi, ct)).Select(p => new SourcePage(p, null)).ToList(),
            _ => [new SourcePage(null, Encoding.UTF8.GetString(data))],
        };
        return new InvoiceSourceResp(name, pages);
    });

    public Task<InvoiceReadingResp> InvoiceReading(string caseId, string invoiceId, CancellationToken ct) => Guard(async () =>
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
        var images = InvoiceParser.Detect(data) switch
        {
            Kind.Image => [data],
            Kind.Pdf when pdf is not null => await pdf.Render(data, Scan.Dpi, ct),
            _ => new List<byte[]>(),
        };
        for (var i = 0; i < pages.Count && i < images.Count; i++) pages[i].Image = images[i];
        return new InvoiceReadingResp(pages);
    });

    public Task<List<MappingCandidate>> SuggestMapping(string caseId, InvoiceLine line, string? supplier, CancellationToken ct) => Guard(() => Task.Run(() =>
        matcher.Suggest(rules.Load(), Gewerbe(caseId), supplier, line)
            .Select(sg => new MappingCandidate(sg.Mapping, sg.Confidence, sg.Kind)).ToList(), ct));

    // Lines imported before a rule or the model existed, and lines an edit set free,
    // get their turn here: what the matcher is sure about is mapped, the rest stays open.
    public Task<Case> MapCase(string caseId, CancellationToken ct) => Guard(() => Task.Run(async () =>
    {
        var c = LoadCase(caseId);
        var before = c.Invoices.SelectMany(i => i.Lines).Select(l => l.MappingId).ToList();
        foreach (var inv in c.Invoices) await MapLines(inv, c.Taxpayer.Gewerbe, true, ct);
        if (!c.Invoices.SelectMany(i => i.Lines).Select(l => l.MappingId).SequenceEqual(before)) SaveCase(c);
        return c;
    }, ct));

    public Task<CalcResp> Calculate(string caseId, CancellationToken ct) => Guard(() =>
    {
        var (rep, _, rahmen) = Compute(LoadCase(caseId));
        return new CalcResp(rep, rahmen);
    });

    public Task<ReportResp> RenderReport(string caseId, bool pdf, CancellationToken ct) => Guard(async () =>
    {
        var c = LoadCase(caseId);
        var (rep, rs, rahmen) = Compute(c);
        var html = Html.Render(c, rs, rep, rahmen);
        if (!pdf) return new ReportResp(html, null, FileName(c.Label, "html"));
        if (printer is null) throw new ServiceError(ErrorCode.Unsupported, "PDF-Ausgabe nicht verfügbar");
        return new ReportResp(html, await printer.Print(html, ct), FileName(c.Label, "pdf"));
    });

    (Model.Report Report, RuleSet Rules, Rahmen? Rahmen) Compute(Case c)
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
        var rahmen = Vergleich.Aufschlag(Sammlung(c.PeriodTo.Year), c.Taxpayer.Gewerbe, rep.Totals.CalculatedRevenueNet);
        // Verglichen wird der Satz des Betriebs, nicht der einer Sparte: die Sammlung staffelt
        // den Aufschlag nach Gewerbeklasse, nicht nach Getränken und Speisen.
        if (rahmen is not null && rahmen.Lage(rep.Totals.Markup) is var lage && lage != Rahmenlage.Im)
            rep.Warnings.Add(new Flag
            {
                Code = "markup-out-of-range",
                Message = $"Rohgewinnaufschlag {Format.Bp(rep.Totals.Markup)} liegt {(lage == Rahmenlage.Unter ? "unter" : "über")} dem Rahmensatz "
                    + $"{rahmen.Von} bis {rahmen.Bis} v.H. der Richtsatzsammlung {rahmen.Jahr} für „{rahmen.Klasse}“",
            });
        return (rep, rs, rahmen);
    }

    // Die Sammlung des Prüfungsjahres, sonst die jüngste davor.
    Sammlung? Sammlung(int year)
    {
        var found = -1;
        foreach (var i in rules.Sammlungen())
            if (i.Year <= year && i.Year > found) found = i.Year;
        return found < 0 ? null : rules.Sammlung(found);
    }

    Case LoadCase(string id)
    {
        if (id == "") throw new ServiceError(ErrorCode.Invalid, "Fall-ID fehlt");
        return cases.Load(id);
    }

    void SaveCase(Case c)
    {
        var t = Clock.Now();
        if (c.Id == "") c.Id = NewId("fall-");
        if (c.CreatedAt == default) c.CreatedAt = t;
        c.UpdatedAt = t;
        cases.Save(c);
    }

    Case Attach(string caseId, Invoice inv, string fileName, byte[] data, List<OcrPage>? reading = null)
    {
        var c = LoadCase(caseId);
        // The document first: storing it clears whatever else the invoice kept.
        if (data.Length > 0) cases.SaveFile(caseId, inv.Id, fileName, data);
        if (reading is { Count: > 0 }) cases.SaveReading(caseId, inv.Id, reading);
        var i = c.Invoices.FindIndex(x => x.Id == inv.Id);
        if (i >= 0) c.Invoices[i] = inv;
        else c.Invoices.Add(inv);
        SaveCase(c);
        return c;
    }

    string Gewerbe(string caseId)
    {
        if (caseId == "") return "";
        try { return cases.Load(caseId).Taxpayer.Gewerbe; }
        catch (CaseNotFoundException) { return ""; }
    }

    async Task<(RuleSet Rules, List<int> Unmapped)> MapLines(Invoice inv, string gewerbe, bool ask, CancellationToken ct)
    {
        var rs = rules.Load();
        var unmapped = new List<int>();
        foreach (var l in inv.Lines)
        {
            if (!string.IsNullOrEmpty(l.MappingId)
                && !(rs.Mappings.TryGetValue(l.MappingId, out var m) && Match.Fits(m, inv.SupplierName, inv.Date ?? Today(), l)))
                l.MappingId = null;
            if (string.IsNullOrEmpty(l.MappingId)) rs = await MapLine(rs, gewerbe, inv, l, ask, ct);
            if (string.IsNullOrEmpty(l.MappingId)) unmapped.Add((int)l.No);
        }
        return (rs, unmapped);
    }

    async Task<RuleSet> MapLine(RuleSet rs, string gewerbe, Invoice inv, InvoiceLine l, bool ask, CancellationToken ct)
    {
        if (Match.Mapping(rs, inv.SupplierName, inv.Date ?? Today(), l) is { } hit)
        {
            l.MappingId = hit.Id;
            return rs;
        }
        if (!ask) return rs;
        var sugs = await Task.Run(() => matcher.Suggest(rs, gewerbe, inv.SupplierName, l), ct);
        if (sugs.Count == 0) return rs;
        var sg = sugs[0];
        if (sg.Kind == OriginKind.Exact)
        {
            l.MappingId = sg.Mapping.Id;
            return rs;
        }
        if (sg.Confidence < AutoMapMinConfidence) return rs;
        var m = sg.Mapping;
        m.Id = NewId("map-");
        m.Meta = new Meta { ChangedAt = Clock.Now() };
        rs.Mappings[m.Id] = m;
        try
        {
            RuleCheck.Validate(rs);
            rs = rules.Save(m);
        }
        catch (Exception e) when (e is RulesException or StoreUnavailableException)
        {
            return rules.Load();
        }
        l.MappingId = m.Id;
        return rs;
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

    static string NewId(string prefix) =>
        prefix + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));

    static Task<T> Guard<T>(Func<T> body) => Guard(() => Task.FromResult(body()));

    static async Task<T> Guard<T>(Func<Task<T>> body)
    {
        try
        {
            return await body();
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

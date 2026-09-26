using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Rulestore;
using static Umsatzschaetzung.Service.Local;

namespace Umsatzschaetzung.Service;

sealed class LocalReports(RuleStore rules, LocalCases cases, IDocuments? documents, IPdfPrinter? printer, string appVersion) : IReports
{
    public Task<CalcResp> Calculate(string caseId, CancellationToken ct) => Guard(ct, () =>
    {
        var (rep, _, rahmen, _) = Compute(cases.Load(caseId));
        return new CalcResp(rep, rahmen);
    });

    public Task<ReportResp> Render(string caseId, bool pdf, CancellationToken ct) => Guard(ct, async () =>
    {
        var c = cases.Load(caseId);
        var (rep, rs, rahmen, sammlung) = Compute(c);
        var html = Html.Render(c, rs, rep, rahmen, sammlung?.Sammlung, sammlung?.Info, appVersion);
        if (!pdf) return new ReportResp(html, null, null);
        if (printer is null) throw new ServiceError(ErrorCode.Unsupported, "PDF-Ausgabe nicht verfügbar");
        if (documents is null) throw new ServiceError(ErrorCode.Unsupported, "Belege lassen sich hier nicht lesen");
        return new ReportResp(html, documents.Stamp(await printer.Print(html, ct), Html.Marks(c, rep)), FileName(c.Label, "pdf"));
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
}

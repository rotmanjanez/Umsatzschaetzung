using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Reports;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(ReportData))]
internal sealed partial class ReportJson : JsonSerializerContext;

public sealed record PageMarks(string TopLeft, string TopRight, IReadOnlyList<string> Footer);

public static class Html
{
    static string Resource(string name)
    {
        using var s = typeof(Html).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException(name + " fehlt");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public const string BuiltinId = "tpl.bericht";

    static EstimateGroups Estimated(List<EstimateRow> rows)
    {
        EstimateGroup Of(EstimateSource source)
        {
            var of = rows.FindAll(e => e.Source == source);
            return new(of, of.Sum(e => e.Cost), of.Sum(e => e.RevenueNet));
        }
        return new(Of(EstimateSource.PriceMissing), Of(EstimateSource.Leftover), Of(EstimateSource.Unused));
    }

    // Die Gastronomie kalkuliert je Sparte, jedes andere Gewerbe in einer Tabelle.
    static List<CalculationGroup> Calculation(Model.Report r)
    {
        var priced = r.Products.FindAll(p => p.Portions > 0 && !p.PriceMissing);
        if (r.Markups.Count == 0)
            return [new(null, priced, r.Totals.PricedCost, r.Totals.Markup)];
        return [.. r.Markups.Select(m => new CalculationGroup(m.Sparte, priced.FindAll(p => p.Sparte == m.Sparte), m.CostOfGoods, m.Markup))];
    }

    static List<SupplierSum> Suppliers(Case c) =>
        [.. c.Invoices
            .GroupBy(i => i.SupplierName)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new SupplierSum(g.Key, g.Count(), g.Sum(i => i.NetTotal), g.Sum(i => i.GrossTotal)))];

    public static PageMarks Marks(Case c, Model.Report r) => new(
        "Umsatzschätzung · " + c.Label,
        Format.Period(c.PeriodFrom, c.PeriodTo),
        [
            "Steuernummer " + c.Taxpayer.TaxNumber,
            "Name des Steuerpflichtigen " + c.Taxpayer.Name,
            "PAB-Nr. " + c.Taxpayer.PabNumber,
            "Datum " + Format.Day(r.ComputedAt),
        ]);

    public static ReportData Data(Case c, RuleSet rs, Model.Report r, Rahmen? rahmen, TemplateInfo template,
        Sammlung? sammlung = null, SammlungInfo? quelle = null, string appVersion = "")
    {
        rs = Recipes.Effective(c, rs);
        var s = r.Totals;
        var included = Included.Of(c, r);
        return new ReportData
        {
            Case = c,
            Rules = rs,
            Report = r,
            Included = included,
            Revenue = VatRow.Of(c, r),
            InvoiceCount = included.Count,
            IncludedNet = s.CostOfGoods + s.StockChange,
            Excluded = s.UnmappedCost + s.NoRevenueCost,
            Estimated = Estimated(r.Estimated),
            Calculation = Calculation(r),
            Rahmen = rahmen,
            Lage = rahmen?.Lage(s.Markup),
            AnyYields = r.Ingredients.Exists(i => i.Yield is not null),
            Gewerbe = rs.Gewerbezweige.Values.FirstOrDefault(g => g.Kennzahl == c.Taxpayer.Gewerbe),
            Richtsatz = sammlung is null ? null
                : new Richtsatzbasis(sammlung.Year, quelle?.Quelle ?? "", quelle?.ImportedAt ?? default,
                    Vergleich.Klasse(sammlung, c.Taxpayer.Gewerbe), sammlung.Pauschbeträge),
            Suppliers = Suppliers(c),
            Marks = Marks(c, r),
            Template = template,
            PeriodDays = c.PeriodTo.DayNumber - c.PeriodFrom.DayNumber + 1,
            GeneratedAt = Clock.Now(),
            AppVersion = appVersion,
        };
    }

    // Eine Prüfung ohne eigene Wahl, oder mit einer gelöschten, nimmt den Standard. Ein Regelstand
    // ganz ohne Vorlage fällt auf die mitgelieferte zurück.
    public static ReportTemplate Pick(Case c, RuleSet rs) =>
        rs.Template(c.TemplateId) ?? new ReportTemplate { Id = BuiltinId, Name = "Bericht", Source = Builtin(), Default = true };

    public static string Builtin() => Resource("bericht.html");

    public static string Render(Case c, RuleSet rs, Model.Report r, Rahmen? rahmen,
        Sammlung? sammlung = null, SammlungInfo? quelle = null, string appVersion = "")
    {
        var t = Pick(c, rs);
        var data = Data(c, rs, r, rahmen, new TemplateInfo(t.Id, t.Name), sammlung, quelle, appVersion);
        return Template.Render(t.Source, JsonSerializer.SerializeToNode(data, ReportJson.Default.ReportData)!.AsObject());
    }

    public static JsonSerializerOptions JsonOptions => ReportJson.Default.Options;
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Reports;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(Case))]
[JsonSerializable(typeof(RuleSet))]
[JsonSerializable(typeof(Model.Report))]
[JsonSerializable(typeof(Rahmen))]
[JsonSerializable(typeof(Rahmenlage))]
[JsonSerializable(typeof(List<Included>))]
[JsonSerializable(typeof(List<VatRow>))]
[JsonSerializable(typeof(List<EstimateRow>))]
[JsonSerializable(typeof(List<ProductRow>))]
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

    static JsonObject Estimated(List<EstimateRow> rows)
    {
        var j = ReportJson.Default;
        var output = new JsonObject();
        foreach (var (key, source) in new[] { ("products", EstimateSource.PriceMissing), ("leftover", EstimateSource.Leftover), ("lines", EstimateSource.Unused) })
        {
            var of = rows.FindAll(e => e.Source == source);
            output[key] = new JsonObject
            {
                ["rows"] = JsonSerializer.SerializeToNode(of, j.ListEstimateRow),
                ["cost"] = of.Sum(e => e.Cost),
                ["revenueNet"] = of.Sum(e => e.RevenueNet),
            };
        }
        return output;
    }

    // Die Gastronomie kalkuliert je Sparte, jedes andere Gewerbe in einer Tabelle.
    static JsonArray Calculation(Model.Report r)
    {
        var j = ReportJson.Default;
        var priced = r.Products.FindAll(p => p.Portions > 0 && !p.PriceMissing);
        JsonObject Group(Sparte? sparte, List<ProductRow> rows, long portions, long markup) => new()
        {
            ["sparte"] = sparte is { } s ? JsonSerializer.SerializeToNode(s, j.Sparte) : null,
            ["rows"] = JsonSerializer.SerializeToNode(rows, j.ListProductRow),
            ["portions"] = portions,
            ["markup"] = markup,
        };
        if (r.Markups.Count == 0)
            return [Group(null, priced, r.Totals.PricedPortions, r.Totals.Markup)];
        return [.. r.Markups.Select(m => Group(m.Sparte, priced.FindAll(p => p.Sparte == m.Sparte), m.Portions, m.Markup))];
    }

    public static PageMarks Marks(Case c, Model.Report r) => new(
        "Umsatzschätzung · " + c.Label,
        Format.Period(c.PeriodFrom, c.PeriodTo),
        [
            "Steuernummer " + c.Taxpayer.TaxNumber,
            "Name des Steuerpflichtigen " + c.Taxpayer.Name,
            "PaB-Nr. " + c.Taxpayer.PabNumber,
            "Datum " + Format.Day(r.ComputedAt),
        ]);

    public static string Render(Case c, RuleSet rs, Model.Report r, Rahmen? rahmen)
    {
        rs = Recipes.Effective(c, rs);
        var s = r.Totals;
        var j = ReportJson.Default;
        var included = Included.Of(c, r);
        var data = new JsonObject
        {
            ["case"] = JsonSerializer.SerializeToNode(c, j.Case),
            ["rules"] = JsonSerializer.SerializeToNode(rs, j.RuleSet),
            ["report"] = JsonSerializer.SerializeToNode(r, j.Report),
            ["included"] = JsonSerializer.SerializeToNode(included, j.ListIncluded),
            ["revenue"] = JsonSerializer.SerializeToNode(VatRow.Of(c, r), j.ListVatRow),
            ["invoiceCount"] = included.Count,
            ["includedNet"] = s.CostOfGoods + s.StockChange,
            ["excluded"] = s.UnmappedCost + s.NoRevenueCost,
            ["estimated"] = Estimated(r.Estimated),
            ["calculation"] = Calculation(r),
            ["rahmen"] = rahmen is null ? null : JsonSerializer.SerializeToNode(rahmen, j.Rahmen),
            ["lage"] = rahmen is null ? null : JsonSerializer.SerializeToNode(rahmen.Lage(s.Markup), j.Rahmenlage),
            ["anyBinding"] = r.Ingredients.Exists(i => i.Binding),
            ["anyYields"] = r.Ingredients.Exists(i => i.Yield is not null),
            ["anyEstimated"] = r.Ingredients.Exists(i => i.Estimated),
            ["anyApproximate"] = r.Allocations.Exists(a => a.Approximate),
        };
        return Template.Render(Resource("bericht.html"), data);
    }
}

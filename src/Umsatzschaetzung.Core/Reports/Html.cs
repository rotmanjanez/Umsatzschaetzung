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
internal sealed partial class ReportJson : JsonSerializerContext;

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
            ["excluded"] = s.UnmappedCost,
            ["estimated"] = Estimated(r.Estimated),
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

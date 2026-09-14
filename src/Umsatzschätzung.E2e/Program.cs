using System.Text;
using Umsatzschätzung.Casefile;
using Umsatzschätzung.Model;
using Umsatzschätzung.Rulestore;
using Umsatzschätzung.Service;

var data = Path.Combine(AppContext.BaseDirectory, "data");
var work = Directory.CreateTempSubdirectory("umsatzschätzung-e2e-");
var store = Path.Combine(work.FullName, "store");
var snapshots = Path.Combine(work.FullName, "snapshots");
var seed = Json.Deserialize<RuleSet>(File.ReadAllBytes(Path.Combine(data, "ruleset.json")));
IService svc = new LocalService(
    new RuleStore(store, snapshots, seed),
    new CaseStore(Path.Combine(work.FullName, "cases")),
    null, null, null, null, "e2e");
var ct = CancellationToken.None;
var checks = 0;

void Check(bool ok, string what)
{
    checks++;
    if (!ok) throw new Exception("FAIL: " + what);
}

var status = await svc.Status(ct);
Check(status.RulesVersion == 0 && status.Problem is null, "status reads the seeded rule set");

var kase = await svc.ImportCase("case.json", File.ReadAllBytes(Path.Combine(data, "case.json")), ct);
Check(kase.Case.Id == "case.bar.2024" && kase.Case.Invoices.Count == 1, "case import");

var calc = await svc.Calculate(kase.Case.Id, ct);
var summary = calc.Summary.ToDictionary(kv => kv.Key, kv => kv.Value);
Check(summary["purchases"] == "1.690,00 €", "purchases " + summary["purchases"]);
Check(summary["costOfGoods"] == "1.513,10 €", "cost of goods " + summary["costOfGoods"]);
Check(summary["stockChange"] == "176,90 €", "stock change " + summary["stockChange"]);
Check(summary["calculatedRevenueNet"] == "7.335,95 €", "revenue " + summary["calculatedRevenueNet"]);
Check(summary["markup"] == "384,82 %", "markup " + summary["markup"]);
Check(summary["portions"] == "2.946 Portionen", "portions " + summary["portions"]);
Check(calc.Excluded.Unmapped.Count == 0 && calc.Excluded.Unused.Count == 0, "nothing excluded");

var report = await svc.RenderReport(kase.Case.Id, false, ct);
Check(report.Html.Contains("7.335,95 €") && report.Html.Contains("Anhang E"), "html report");

var csv = Encoding.UTF8.GetString((await svc.ExportCase(kase.Case.Id, ExportFormat.Csv, ct)).Data);
Check(csv.Contains("Kalkulierter Umsatz (netto);7.335,95 €"), "csv export");

var parsed = await svc.ParseInvoice(kase.Case.Id, "zugferd.pdf", File.ReadAllBytes(Path.Combine(data, "zugferd.pdf")), ct);
Check(!parsed.NeedsOcr && parsed.Invoice.Number == "RE-20201121/508" && parsed.Invoice.Lines.Count == 3, "zugferd parse");
Check(parsed.Case is { Case.Invoices.Count: 2 } && parsed.UnmappedLines.Count == 3, "zugferd attached, lines unmapped");
try
{
    await svc.InvoiceSource(kase.Case.Id, parsed.Invoice.Id, ct);
    Check(false, "pdf preview needs a renderer");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Unsupported, "pdf preview without renderer is Unsupported, got " + e.Code);
}

var korn = (await svc.Rules(ct)).RuleSet.Products["prod.korn.4cl"];
korn.Meta.ValidTo = new DateOnly(2024, 6, 30);
var saved = await svc.SaveRule(korn, ct);
Check(saved.RuleSet.Version == 1 && saved.RuleSet.Products["prod.korn.4cl"].Meta.Rev == 1, "save bumps version and stamps the entity");
var water = new Ingredient { Id = "ing.wasser", Name = "Mineralwasser", BaseUnit = Unit.Ml, Category = "Alkoholfrei" };
var merged = (await svc.SaveRule(water, ct)).RuleSet;
Check(merged.Version == 2 && merged.Ingredients.ContainsKey("ing.wasser") && merged.Products["prod.korn.4cl"].Meta.ValidTo is not null, "saves accumulate per entity");
try
{
    await svc.SaveRule(new Product { Id = "prod.kaputt", Name = "Kaputt", Recipe = [new RecipeLine { IngredientId = "ing.fehlt", Amount = 1 }] }, ct);
    Check(false, "invalid rule must be rejected");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Invalid, "invalid rule rejected with Invalid, got " + e.Code);
}
var recalced = await svc.Calculate(kase.Case.Id, ct);
Check(recalced.Products.All(p => p.ProductId != "prod.korn.4cl"), "retired product leaves the calculation");

RuleStore Reopen() => new(store, snapshots, seed);
Reopen();
Check(Directory.GetFiles(snapshots).Length == 1, "second start snapshots the store");
File.WriteAllText(Path.Combine(store, "rules.db"), "kaputt");
var restored = Reopen();
Check(restored.Notice is not null && restored.Load().Version == 2, "corrupt store restored from snapshot");
Check(Directory.GetFiles(store, "rules.db.defekt-*").Length == 1, "corrupt file kept aside");

work.Delete(true);
Console.WriteLine($"ok, {checks} checks");

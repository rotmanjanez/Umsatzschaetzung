using System.Text;
using Umsatzschätzung.Casefile;
using Umsatzschätzung.Model;
using Umsatzschätzung.Rulestore;
using Umsatzschätzung.Service;
using Umsatzschätzung.E2e;
using Umsatzschätzung.Suggest;
using Umsatzschätzung.Tagging;

if (args is ["tokenizer", var tokenizerDir, ..])
    return TokenizerParity.Run(tokenizerDir, args.Length > 2 ? args[2] : null);

var data = Path.Combine(AppContext.BaseDirectory, "data");
var work = Directory.CreateTempSubdirectory("umsatzschätzung-e2e-");
var store = Path.Combine(work.FullName, "store");
var snapshots = Path.Combine(work.FullName, "snapshots");
var seed = Json.Deserialize<RuleSet>(File.ReadAllBytes(Path.Combine(data, "ruleset.json")));
IService svc = new LocalService(
    new RuleStore(store, snapshots, seed),
    new CaseStore(Path.Combine(work.FullName, "cases")),
    null, new Tagger(), null, null, "e2e");
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

var neu = await svc.PutCase(new Case
{
    Label = "Neue Prüfung",
    PeriodFrom = new DateOnly(2024, 1, 1),
    PeriodTo = new DateOnly(2024, 12, 31),
    Taxpayer = new Taxpayer { Name = "Muster", TaxNumber = "123/4567", PabNumber = "89" },
}, ct);
Check(neu.Case.Id.StartsWith("fall-") && (await svc.ListCases(ct)).Cases.Any(c => c.Case.Id == neu.Case.Id),
    "a case without an id gets one and is stored");

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

Pack? Size(string text) => PackSize.Read(text);
Check(Size("Kiste Pils 20 x 0,5 l") == new Pack(20, 500, Unit.Ml), "pack: count times litres");
Check(Size("Mehl Type 550 25 kg Sack") == new Pack(1, 25000, Unit.G), "pack: kilograms, article number is not a size");
Check(Size("Weisswein trocken 0,75 l 12% vol") == new Pack(1, 750, Unit.Ml), "pack: alcohol strength is not a size");
Check(Size("Cola PET 24x0,33") == new Pack(24, 330, null), "pack: unitless count times value reads as litres");
Check(Size("Servietten 3-lagig 250 Stk") == new Pack(250, 1000, Unit.Piece), "pack: piece count");
Check(Size("Trg 6er Limo 0,33 l") == new Pack(6, 330, Unit.Ml), "pack: multipack prefix ahead of the size");
Check(Size("Rinderhuefte, Abrechnung je kg") is null, "pack: nothing to read");
Check(PackSize.Strip("Kiste Pils 20 x 0,5 l") == "Pils", "strip: packaging goes, the ware stays");
Check(PackSize.Strip("Mehl Type 550 25 kg Sack") == "Mehl Type 550", "strip: a number outside a pack size is not packaging");
Check(Matcher.Factor(null, "KGM", Unit.G) is null, "factor: kg needs none, the unit table converts");
Check(Matcher.Factor(new Pack(20, 500, Unit.Ml), "XCS", Unit.Ml) == 10000, "factor: a crate is only known from its pack size");
Check(Matcher.Factor(new Pack(1, 750, Unit.Ml), "XBO", Unit.G) is null, "factor rejects a size in the wrong base unit");

async Task<List<MappingCandidate>> Suggest(string name, string unitCode) =>
    (await svc.SuggestMapping(new InvoiceLine { Name = name, UnitCode = unitCode }, "Rheinland Getränke Fachgroßhandel GmbH", ct)).Candidates;

var keg = await Suggest("Fassbier Pils, Keg 50 l", "XKG");
Check(keg.Count > 0 && keg[0].Mapping.IngredientId == "ing.bier.fass" && keg[0].Mapping.Factor == 50000,
    "suggest: keg maps to the draught beer");
Check(keg[0].Kind == OriginKind.Lexical && keg[0].Confidence is > 0 and < 100, "suggest: candidate is lexical and not certain");
Check(keg[0].Mapping.Id == "", "suggest: a lexical candidate is a proposal, not a stored mapping");

var schnaps = await Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO");
Check(schnaps.Count > 0 && schnaps[0].Mapping.IngredientId == "ing.korn" && schnaps[0].Mapping.Factor == 700,
    "suggest: bottle size beats the alcohol strength");

Check((await Suggest("Pfand Leergut Kiste", "XCS")).Count == 0, "suggest: deposit has no ingredient");
Check((await Suggest("Fassbier Pils, Keg 50 l", "XKG"))[0].Mapping.Factor == 50000, "suggest: index is reused");

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
var alkoholfrei = new Category { Id = "cat.alkoholfrei", Name = "Alkoholfrei" };
Check((await svc.SaveRule(alkoholfrei, ct)).RuleSet.Categories.ContainsKey("cat.alkoholfrei"), "a category is a rule entity of its own");
var water = new Ingredient { Id = "ing.wasser", Name = "Mineralwasser", CategoryId = "cat.alkoholfrei" };
var merged = (await svc.SaveRule(water, ct)).RuleSet;
Check(merged.Version == 3 && merged.Ingredients.ContainsKey("ing.wasser") && merged.Products["prod.korn.4cl"].Meta.ValidTo is not null, "saves accumulate per entity");
try
{
    await svc.SaveRule(new Ingredient { Id = "ing.kaputt", Name = "Kaputt", CategoryId = "cat.fehlt" }, ct);
    Check(false, "ingredient with a dangling category must be rejected");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Invalid, "dangling category rejected with Invalid, got " + e.Code);
}
try
{
    await svc.SaveRule(new Product { Id = "prod.kaputt", Name = "Kaputt", Recipe = [new RecipeLine { IngredientId = "ing.fehlt", Amount = 1 }] }, ct);
    Check(false, "invalid rule must be rejected");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Invalid, "invalid rule rejected with Invalid, got " + e.Code);
}
try
{
    await svc.DeleteRule(Entity.Ingredient, "ing.korn", ct);
    Check(false, "an ingredient other entries reference must not be deletable");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Conflict, "referenced ingredient rejected with Conflict, got " + e.Code);
}
var pruned = (await svc.DeleteRule(Entity.Product, "prod.korn.2cl", ct)).RuleSet;
Check(pruned.Version == 4 && !pruned.Products.ContainsKey("prod.korn.2cl"), "delete drops the entity and bumps the version");

var recalced = await svc.Calculate(kase.Case.Id, ct);
Check(recalced.Products.All(p => p.ProductId != "prod.korn.4cl"), "retired product leaves the calculation");

RuleStore Reopen() => new(store, snapshots, seed);
Check(!Reopen().Load().Products.ContainsKey("prod.korn.2cl"), "the seed does not resurrect a deleted entity");
Check(Directory.GetFiles(snapshots).Length == 1, "second start snapshots the store");
File.WriteAllText(Path.Combine(store, "rules.db"), "kaputt");
var restored = Reopen();
Check(restored.Notice is not null && restored.Load().Version == 4, "corrupt store restored from snapshot");
Check(Directory.GetFiles(store, "rules.db.defekt-*").Length == 1, "corrupt file kept aside");

// Dynamic ranking: a confirmed mapping teaches the wording, and the wording carries
// to a line nobody has seen, with its own pack size.
Check((await Suggest("Zwickl naturtrueb, Keg 50 l", "XKG")).Count == 0, "suggest: unknown wording maps to nothing");
await svc.SaveRule(new ArticleMapping
{
    Id = "map.zwickl",
    SupplierName = "Rheinland Getränke Fachgroßhandel GmbH",
    SupplierArticleId = "Z-1",
    Observed = "Zwickl naturtrueb, Keg 30 l",
    IngredientId = "ing.bier.fass",
    Factor = 30000,
    Confirmed = true,
}, ct);
var learned = await Suggest("Zwickl naturtrueb, Keg 50 l", "XKG");
Check(learned.Count > 0 && learned[0].Mapping.IngredientId == "ing.bier.fass" && learned[0].Mapping.Factor == 50000,
    "suggest: one confirmation teaches the wording and the new pack size still decides the factor");
Check((await Suggest("Pfand Leergut Kiste", "XCS")).Count == 0, "suggest: what was learnt does not drag the deposit line along");

work.Delete(true);
Console.WriteLine($"ok, {checks} checks");

return 0;

using System.Text;
using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.E2e;
using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Tagging;

if (args is ["tokenizer", var tokenizerDir, ..])
    return TokenizerParity.Run(tokenizerDir, args.Length > 2 ? args[2] : null);

var data = Path.Combine(AppContext.BaseDirectory, "data");
var work = Directory.CreateTempSubdirectory("umsatzschätzung-e2e-");
var store = Path.Combine(work.FullName, "store");
var seed = Json.Deserialize<RuleSet>(File.ReadAllBytes(Path.Combine(data, "ruleset.json")));
var cases = new CaseStore(Path.Combine(work.FullName, "cases"));
IService svc = new LocalService(new RuleStore(store, seed), cases, null, new Tagger(), null, null, "e2e");
var ct = CancellationToken.None;
var checks = 0;

void Check(bool ok, string what)
{
    checks++;
    if (!ok) throw new Exception("FAIL: " + what);
}

static Case Fixture(string sql, string id)
{
    var dir = Directory.CreateTempSubdirectory("umsatzschätzung-vorlage-").FullName;
    var store = new CaseStore(dir);
    store.Save(new Case
    {
        Id = id,
        Label = "Vorlage",
        PeriodFrom = new DateOnly(2024, 1, 1),
        PeriodTo = new DateOnly(2024, 12, 31),
        Taxpayer = new Taxpayer { Name = "-", TaxNumber = "-", PabNumber = "-" },
        CreatedAt = Clock.Now(),
        UpdatedAt = Clock.Now(),
    });
    using (var db = new SqliteConnection(new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dir, id + ".db"),
        Pooling = false,
    }.ToString()))
    {
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = File.ReadAllText(sql);
        cmd.ExecuteNonQuery();
    }
    return store.Load(id);
}

var status = await svc.Status(ct);
Check(status.RulesVersion == 0 && status.Problem is null, "status reads the seeded rule set");

var kase = await svc.PutCase(Fixture(Path.Combine(data, "case.sql"), "case.bar.2024"), ct);
Check(kase.Case.Id == "case.bar.2024" && kase.Case.Invoices.Count == 1, "case stored");

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

Check(Parse.UnitCode("Fl") == "XBO" && Parse.UnitCode("kg") == "KGM", "unit: the table resolves exactly");
Check(Parse.UnitCode("F1") == "XBO", "unit: a bottle read with a one comes back");
Check(Parse.UnitCode("17F1") == "XBO", "unit: the quantity column bleeding in is stripped");
Check(Parse.UnitCode("1") == "1" && Parse.UnitCode("0") == "0", "unit: a lone digit is a stray, not a litre");
Check(Parse.UnitCode("Zi") == "Zi", "unit: an unknown short code still passes through");
Check(Units.NoFoldCollisions, "unit: no two aliases in units.json fold together");

Check(Parse.PackUnit("Frittieröl 10 1") == "Frittieröl 10 l", "pack unit: a litre read as a one");
Check(Parse.PackUnit("Bratwurst grob, fränkisch, 120 8") == "Bratwurst grob, fränkisch, 120 g",
    "pack unit: a gram read as an eight");
Check(Parse.PackUnit("Trg 6er Limo 8 x 0,33 l") == "Trg 6er Limo 8 x 0,33 l",
    "pack unit: an eight that is not a unit letter is left alone");
Check(Parse.PackUnit("8 Stück Semmeln") == "8 Stück Semmeln", "pack unit: nothing before it, nothing to fix");

static InvoiceLine Regrouped(string text, long quantity, long unitPrice, long lineNet)
{
    var line = new InvoiceLine { Quantity = quantity, UnitPrice = unitPrice, LineNet = lineNet, PriceBaseQty = 1000 };
    Assemble.Regroup(line, text);
    return line;
}

Check(Regrouped("5,450", 5450000, 12000000, 6540).Quantity == 5450, "regroup: a lost decimal comma is taken back by the line net");
Check(Regrouped("4 670", 4670000, 6900000, 3222).Quantity == 4670, "regroup: a separator the scan dropped arrives as two words");
Check(Regrouped("134", 134000, 340000, 4556).Quantity == 134000, "regroup: a line that already adds up is left alone");
Check(Regrouped("5450", 5450000, 12000000, 6540).Quantity == 5450000, "regroup: a quantity written without a separator is never regrouped");
Check(Regrouped("1,25", 1250, 12000000, 1500).Quantity == 1250, "regroup: a two-digit decimal is not the ambiguous shape");
Check(Regrouped("5,450", 5450000, 12000000, 0).Quantity == 5450000, "regroup: without a line net nothing is inferred");
Check(Regrouped("5,450", 5450000, 12000000, 9999).Quantity == 5450000, "regroup: a line that adds up neither way is left alone");

static InvoiceLine Repaired(long quantity, long unitPrice, long lineNet)
{
    var line = new InvoiceLine { Quantity = quantity, UnitPrice = unitPrice, LineNet = lineNet, PriceBaseQty = 1000 };
    Assemble.Repair(line);
    return line;
}

Check(Repaired(6000, 900000, 810).Quantity == 9000, "repair: a nine read as a six is put back by the row");
Check(Repaired(3750, 5900000, 2588).UnitPrice == 6900000, "repair: a six read as a five in the unit price");
Check(Repaired(134000, 340000, 4556).Quantity == 134000, "repair: a row that adds up is never touched");
Check(Repaired(13640, 6200000, 8467).LineNet == 8457, "repair: a six read as a seven in the line net");
// 6000 x 1,00 reads 8,00: the eight could be the line net (6,00) or the quantity (8000).
var ambiguous = Repaired(6000, 1000000, 800);
Check(ambiguous.Quantity == 6000 && ambiguous.LineNet == 800,
    "repair: two digits explain the row equally well, so it is left wrong");

static List<Flag> Checked(string unitCode)
{
    var inv = new Invoice { Number = "1", SupplierName = "X", Date = new DateOnly(2025, 1, 1), StatedNet = 500, StatedGross = 595 };
    inv.Lines.Add(new InvoiceLine { No = 1, Name = "X", Quantity = 1000, UnitPrice = 5000000, LineNet = 500, Vat = 1900, PriceBaseQty = 1000, UnitCode = unitCode });
    return Umsatzschaetzung.Extract.Check.Invoice(inv);
}

Check(Checked("KGM").Count == 0, "check: a line that states its unit and adds up is clean");
Check(Checked("").Any(f => f.Code == "no_unit"), "check: a line without a unit is reported");
Check(!Umsatzschaetzung.Extract.Check.Complete(new Invoice(), Checked("")), "check: and such an invoice is not complete");
Check(Repaired(5000, 1234567, 9999).Quantity == 5000, "repair: no single digit explains the row");
Check(Repaired(0, 900000, 810).Quantity == 0, "repair: an unread cell is not guessed at");

static SkiaSharp.SKBitmap Ruled(double lean)
{
    var page = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(600, 800,
        SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
    using (var canvas = new SkiaSharp.SKCanvas(page))
    {
        canvas.Clear(SkiaSharp.SKColors.White);
        using var paint = new SkiaSharp.SKPaint { Color = SkiaSharp.SKColors.Black };
        for (var y = 60; y < 740; y += 24)
            for (var x = 60; x < 540; x += 40)
                canvas.DrawRect(x, y, 28, 8, paint);
    }
    if (lean == 0) return page;
    using (page) return Deskew.Straighten(page, lean);
}

using (var flat = Ruled(0))
{
    Check(Math.Abs(Deskew.Angle(flat)) < 0.05, "deskew: a straight page reads as straight");
    Check(Deskew.Apply(flat) is null, "deskew: a straight page is not resampled");
}
foreach (var lean in new[] { -3.0, -0.8, 0.8, 3.0 })
{
    using var page = Ruled(lean);
    Check(Math.Abs(Deskew.Angle(page) + lean) < 0.1, $"deskew: recovers a {lean} degree lean");
}
using (var leaning = Ruled(2.0))
using (var straight = Deskew.Apply(leaning))
{
    Check(straight is not null, "deskew: a leaning page is straightened");
    Check(Math.Abs(Deskew.Angle(straight!)) < 0.05, "deskew: straightening leaves no lean");
}

// A sheet printed both sides: the front in solid ink, the reverse showing through faintly
// and mirrored. Only the front may survive.
static SkiaSharp.SKBitmap Doubled()
{
    var page = new SkiaSharp.SKBitmap(new SkiaSharp.SKImageInfo(600, 800,
        SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul));
    using var canvas = new SkiaSharp.SKCanvas(page);
    canvas.Clear(SkiaSharp.SKColors.White);
    using var ghost = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(205, 205, 205) };
    for (var y = 70; y < 730; y += 24)
        for (var x = 330; x < 560; x += 40)
            canvas.DrawRect(x, y, 26, 7, ghost);
    using var front = new SkiaSharp.SKPaint { Color = new SkiaSharp.SKColor(20, 20, 20) };
    for (var y = 60; y < 740; y += 24)
        for (var x = 40; x < 300; x += 40)
            canvas.DrawRect(x, y, 28, 8, front);
    return page;
}

static (int Front, int Ghost) Survivors(SkiaSharp.SKBitmap page)
{
    int front = 0, ghost = 0;
    for (var y = 0; y < page.Height; y++)
        for (var x = 0; x < page.Width; x++)
        {
            if (page.GetPixel(x, y).Red >= 250) continue;
            if (x < 310) front++; else ghost++;
        }
    return (front, ghost);
}

using (var sheet = Doubled())
using (var cleaned = Deink.Apply(sheet))
{
    Check(cleaned is not null, "deink: a page carrying show-through is cleaned");
    var before = Survivors(sheet);
    var after = Survivors(cleaned!);
    Check(after.Ghost == 0, $"deink: the reverse is gone, {before.Ghost} -> {after.Ghost}");
    Check(after.Front >= before.Front * 0.95,
        $"deink: the front survives whole, {before.Front} -> {after.Front}");
}

using (var flat = Ruled(0))
using (var cleaned = Deink.Apply(flat))
{
    var kept = cleaned is null ? Survivors(flat) : Survivors(cleaned);
    Check(kept.Front >= Survivors(flat).Front * 0.95, "deink: a single-sided page keeps its ink");
}

async Task<List<MappingCandidate>> Suggest(string name, string unitCode) =>
    (await svc.SuggestMapping("", new InvoiceLine { Name = name, UnitCode = unitCode }, "Rheinland Getränke Fachgroßhandel GmbH", ct)).Candidates;

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

var friseur = await svc.PutCase(new Case
{
    Label = "Salon",
    PeriodFrom = new DateOnly(2024, 1, 1),
    PeriodTo = new DateOnly(2024, 12, 31),
    Taxpayer = new Taxpayer { Name = "Schnitt", TaxNumber = "1/2", PabNumber = "3", Gewerbe = "96021.0" },
}, ct);
var line = new InvoiceLine { Name = "Doppelkorn 38 % vol, Flasche 0,7 l", UnitCode = "XBO" };
Check((await svc.SuggestMapping(friseur.Case.Id, line, null, ct)).Candidates.Count == 0, "suggest: a Friseur is never offered Korn");
Check((await svc.SuggestMapping(neu.Case.Id, line, null, ct)).Candidates[0].Mapping.IngredientId == "ing.korn", "suggest: a case without Gewerbe sees everything");

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

var dump = await svc.ExportCase(kase.Case.Id, ExportFormat.Case, ct);
var back = await svc.ImportCase(dump.FileName, dump.Data, ct);
Check(dump.FileName.EndsWith(".db") && back.Case.Id == kase.Case.Id && back.Case.Invoices.Count == 2,
    "a case exports and imports as one file");
Check(cases.LoadFile(kase.Case.Id, parsed.Invoice.Id).Name == "zugferd.pdf", "the document travels inside it");
try
{
    await svc.ImportCase("kaputt.db", [1, 2, 3], ct);
    Check(false, "a file that is not a case must be refused");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Invalid, "broken case file refused with Invalid, got " + e.Code);
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

RuleStore Reopen() => new(store, seed);
Check(!Reopen().Load().Products.ContainsKey("prod.korn.2cl"), "the seed does not resurrect a deleted entity");
Check(Directory.GetFiles(Path.Combine(store, "snapshots")).Length == 1, "second start snapshots the store");
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

var sammlungen = await svc.Sammlungen(ct);
Check(sammlungen.Sammlungen.Count > 0 && sammlungen.Sammlungen.TrueForAll(s => s.Mitgeliefert),
    "richtsatz: the shipped Sammlungen seed themselves");
Check(sammlungen.Sammlungen[0].Year > sammlungen.Sammlungen[^1].Year, "richtsatz: newest year first");
var jüngste = sammlungen.Sammlungen[0].Year;
var nach = await svc.DeleteSammlung(jüngste, ct);
Check(nach.Sammlungen.Count == sammlungen.Sammlungen.Count - 1, "richtsatz: a Sammlung can be dropped");
try
{
    await svc.ImportSammlung("kaputt.pdf", [1, 2, 3], ct);
    Check(false, "richtsatz: a broken PDF is refused");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Invalid, "richtsatz: a broken PDF is refused");
}
var pdfPfad = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "fixtures", "richtsatzsammlung", $"richtsatzsammlung-{jüngste}.pdf");
if (File.Exists(pdfPfad))
{
    var importiert = await svc.ImportSammlung(Path.GetFileName(pdfPfad), File.ReadAllBytes(pdfPfad), ct);
    var wieder = importiert.Sammlungen.Find(s => s.Year == jüngste);
    Check(wieder is { Mitgeliefert: false, Klassen: > 0 } && wieder.Quelle == Path.GetFileName(pdfPfad),
        "richtsatz: an imported PDF takes the place of its year");
    Check((await svc.DeleteSammlung(jüngste, ct)).Sammlungen.TrueForAll(s => s.Year != jüngste), "richtsatz: the import can be dropped again");
}
Check(new RuleStore(store, seed).Sammlungen().Exists(s => s.Year == jüngste && s.Mitgeliefert),
    "richtsatz: a dropped Sammlung is seeded again on the next start");

// Zwei gleichzeitig startende Instanzen legen denselben Speicher an: der zweite darf
// die Schritte des ersten nicht wiederholen.
var gleichzeitig = Path.Combine(work.FullName, "gleichzeitig");
var start = new Barrier(2);
var fehler = new List<Exception>();
var starter = Enumerable.Range(0, 2).Select(_ => new Thread(() =>
{
    try
    {
        start.SignalAndWait();
        var rs = new RuleStore(gleichzeitig, seed).Load();
        if (rs.Products.Count != seed.Products.Count) throw new Exception("Regelsatz unvollständig");
    }
    catch (Exception e)
    {
        lock (fehler) fehler.Add(e);
    }
})).ToList();
starter.ForEach(t => t.Start());
starter.ForEach(t => t.Join());
Check(fehler.Count == 0, "two stores open one directory at once: " + string.Join(" | ", fehler.Select(e => e.Message)));

work.Delete(true);
Console.WriteLine($"ok, {checks} checks");

return 0;

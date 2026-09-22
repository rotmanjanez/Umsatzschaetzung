using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.E2e;
using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Tagging;

if (args is ["tokenizer", var tokenizerDir, ..])
    return TokenizerParity.Run(tokenizerDir, args.Length > 2 ? args[2] : null);

var data = Path.Combine(AppContext.BaseDirectory, "data");
var work = Directory.CreateTempSubdirectory("umsatzschätzung-e2e-");
var store = Path.Combine(work.FullName, "store");
var seed = Json.Deserialize<RuleSet>(File.ReadAllBytes(Path.Combine(data, "ruleset.json")));
var cases = new CaseStore(Path.Combine(work.FullName, "cases"));
var ruleStore = new RuleStore(store, seed);
IService svc = new LocalService(ruleStore, cases, null, new Tagger(), null, null, "e2e");
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
Check(kase.Id == "case.bar.2024" && kase.Invoices.Count == 1, "case stored");

var neu = await svc.PutCase(new Case
{
    Label = "Neue Prüfung",
    PeriodFrom = new DateOnly(2024, 1, 1),
    PeriodTo = new DateOnly(2024, 12, 31),
    Taxpayer = new Taxpayer { Name = "Muster", TaxNumber = "123/4567", PabNumber = "89" },
}, ct);
Check(neu.Id.StartsWith("fall-") && (await svc.ListCases(ct)).Any(c => c.Id == neu.Id),
    "a case without an id gets one and is stored");

var rules = await svc.Rules(ct);
var calc = await svc.Calculate(kase.Id, ct);
// Der Dienst rechnet in Cent und Basispunkten; geschrieben wird erst hier.
var totals = calc.Report.Totals;
Check(Format.Cents(totals.Purchases) == "1.690,00 €", "purchases " + Format.Cents(totals.Purchases));
Check(Format.Cents(totals.CostOfGoods) == "1.513,10 €", "cost of goods " + Format.Cents(totals.CostOfGoods));
Check(Format.Cents(totals.StockChange) == "176,90 €", "stock change " + Format.Cents(totals.StockChange));
Check(Format.Cents(totals.CalculatedRevenueNet) == "7.335,95 €", "revenue " + Format.Cents(totals.CalculatedRevenueNet));
Check(Format.Bp(totals.Markup) == "404,56 %", "markup " + Format.Bp(totals.Markup));
Check(Format.Cents(totals.ShrinkageCost) == "59,15 €" && Format.Cents(totals.UnallocatedCost) == "0,04 €"
    && Format.Cents(totals.AllocatedCost) == "1.453,91 €",
    "the cost of goods reaches the portions through the yield rules and the allocation " + Format.Cents(totals.AllocatedCost));
Check(Format.Portions(totals.Portions) == "2.946 Portionen", "portions " + Format.Portions(totals.Portions));
Check(calc.Report.Unmapped.Count == 0 && calc.Report.Unused.Count == 0, "nothing excluded");

var drinks = calc.Report.Markups.Find(m => m.Sparte == Sparte.Getränke)!;
Check(Format.Cents(drinks.CostOfGoods) == "1.453,91 €" && Format.Cents(drinks.RevenueNet) == "7.335,95 €"
    && Format.Bp(drinks.Markup) == "404,56 %", "markup of the drinks division " + Format.Bp(drinks.Markup));
var beer = calc.Report.Products.Find(p => Names.Product(rules, p.ProductId) == "Pils 0,3 l vom Fass")!;
Check(beer is { Sparte: Sparte.Getränke } && Format.Cents(beer.CostPerPortion) == "0,55 €" && Format.Bp(beer.Markup) == "382,88 %",
    "each recipe carries its own markup, measured against what its ingredients cost");

var pils = calc.Report.Ingredients.Find(i => i.Name == "Fassbier Pils")!;
Check(pils.Purchases.Count > 0 && pils.Purchases.TrueForAll(p => p.Invoice == Names.Invoice(kase, p.InvoiceId) && p.Qty > 0)
    && pils.Bought == pils.Purchases.Sum(p => p.Qty) && pils.Cost == pils.Purchases.Sum(p => p.Net),
    "every ingredient carries the invoice lines it was bought with");
Check(pils.Yield is { Name.Length: > 0 } && pils.YieldRate < Bp.Full && pils.Sellable == pils.Used * pils.YieldRate / Bp.Full
    && Format.Qty(pils.Used, pils.Unit) == Format.Qty(pils.Opening + pils.Bought - pils.Closing, pils.Unit),
    "every ingredient carries the yield rule that was applied and the stock it was moved by");

var rahmen = Vergleich.Aufschlag(ruleStore.Sammlung(2023), "56101.0", 12_000_000);
Check(rahmen is { Von: 178, Bis: 400 } && rahmen.Klasse.StartsWith("Gast-"), "the Gewerbekennzahl finds its Rahmensatz");
Check(rahmen!.Lage(25700) == Rahmenlage.Im && rahmen.Lage(45000) == Rahmenlage.Über && rahmen.Lage(10000) == Rahmenlage.Unter, "a markup is read against the Rahmensatz");
Check(Vergleich.Aufschlag(ruleStore.Sammlung(2023), "561", 12_000_000) is null, "a Kennzahl that fits several Gewerbeklassen has no Rahmensatz");

Check(Html.Render(kase, rules, calc.Report, rahmen).Contains(
    "Richtsatzsammlung 2023, „Gast-, Speise- und Schankwirtschaften“: Rohaufschlag 178 bis 400 % (Mittel 257 %), kalkuliert über dem Rahmen."),
    "the report measures the calculated markup against the Rahmensatz");

var report = await svc.RenderReport(kase.Id, false, ct);
Check(report.Html.Contains("7.335,95 €") && report.Html.Contains("Anhang E"), "html report");
var tpl = new System.Text.Json.Nodes.JsonObject
{
    ["titel"] = "Bier & <Brot>",
    ["zeilen"] = new System.Text.Json.Nodes.JsonArray("a", "b"),
    ["leer"] = new System.Text.Json.Nodes.JsonArray(),
    ["betrag"] = 733595L,
    ["satz"] = 40456L,
    ["menge"] = 20000L,
    ["basis"] = 5000L,
    ["code"] = "LTR",
    ["lage"] = "über",
};
Check(Template.Render("<h1>{{ titel }}</h1>", tpl) == "<h1>Bier &amp; &lt;Brot&gt;</h1>", "template escapes what it writes");
Check(Template.Render("{{ titel | css }}", tpl) == "\"Bier & <Brot>\"", "template writes a CSS string where the template says so");
Check(Template.Render("{{ betrag | cents }} · {{ satz | bp }}", tpl) == "7.335,95 € · 404,56 %", "template formats raw values");
Check(Template.Render("{{ betrag | price:basis:code }}", tpl) == "0,733595 € je 5 Liter", "a filter takes several arguments");
Check(Template.Render("{{ menge | quantity:code }}", tpl) == "20 Liter", "a filter reads its argument from the data");
try { Template.Render("{{ betrag | kilo }}", tpl); Check(false, "unknown filter"); }
catch (TemplateError) { Check(true, "an unknown filter is an error, not an empty cell"); }
Check(Template.Render("{% for z in zeilen %}<li>{{ z }}</li>\n{% endfor %}", tpl) == "<li>a</li>\n<li>b</li>\n", "template repeats a list");
Check(Template.Render("{% if leer %}da{% else %}nichts{% endif %}", tpl) == "nichts", "an empty list is false");
Check(Template.Render("{% if lage == \"über\" %}ja{% else %}nein{% endif %}", tpl) == "ja"
    && Template.Render("{% if lage == \"unter\" %}ja{% else %}nein{% endif %}", tpl) == "nein", "a condition compares against a literal");
try { Template.Render("{{ fehlt }}", tpl); Check(false, "unknown path"); }
catch (TemplateError) { Check(true, "an unknown path is an error, not an empty cell"); }
Check(report.Html.Contains("<h1>3 Rohgewinnaufschlag</h1>") && report.Html.Contains("404,56 %"), "the report carries the markup section");
Check(report.Html.Contains("1.453,91 € × (100 % + 404,56 %) ≈ 7.335,95 €"),
    "the markup carries the calculated revenue back out of the cost of goods");

var csv = Encoding.UTF8.GetString((await svc.ExportInvoice(kase.Id, "inv.bar.1", ct)).Data);
Check(csv.Contains("Rechnungsnummer;2024-04711") && csv.Contains("Netto;1.690,00 €"), "invoice csv carries the header");
Check(csv.Contains("1;Pils Fass 50 l;31090;;12 Keg;92,50 €;1.110,00 €;19 %;Fassbier Pils × 50 l"), "invoice csv carries the lines with their mapping");

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
Check(Parse.UnitCode("EI") == "XBO", "unit: a bottle whose F lost its bar comes back");
Check(Units.NoFoldCollisions, "unit: no two aliases in units.json fold together");

Check(Parse.Digits("1O,5O €") == "10,50 €", "digits: letters in a numeric cell read as the digits they look like");
Check(Parse.Digits("12 Stk") == "12 Stk" && Parse.Digits("Summe") == "Summe", "digits: a word stays a word");
Check(Parse.Digits("l") == "1", "digits: a lone l in a quantity cell is a one");

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
Check(Repaired(0, 900000, 810).Quantity == 9000, "restore: a quantity the scan lost comes back from price and net");
Check(Repaired(0, 3000000, 1000).Quantity == 0, "restore: only where the division comes out clean");
Check(Repaired(3000, 0, 1290).UnitPrice == 4300000, "restore: a lost unit price, in whole cents");
Check(Repaired(3000, 0, 1145).UnitPrice == 0, "restore: a unit price that is not whole cents is not one");
Check(Repaired(3000, 4300000, 0).LineNet == 0, "restore: the line net is never derived");
// 46,92 over 58 kg is no price at all, over the 68 kg printed it is 0,69.
var rescued = Repaired(58000, 0, 4692);
Check(rescued.Quantity == 68000 && rescued.UnitPrice == 690000,
    "restore: the surviving cell is read again where only one confusion of it divides out");
// 12,00 over 7 reads nothing; over 1 it is 12,00 and over 2 it is 6,00.
var unclear = Repaired(7000, 0, 1200);
Check(unclear.Quantity == 7000 && unclear.UnitPrice == 0,
    "restore: two confusions divide out equally well, so the row is left as read");

Check(Table.Join(Field.Quantity, "4.", ",670") == "4,670",
    "join: a number split across two cells keeps the separator the second cell brought");
Check(Parse.Number(Table.Join(Field.Quantity, "4.", ",670"), Parse.ScaleMilli) == 4670,
    "join: and reads as 4,670 kg, not as 4.670");
Check(Table.Join(Field.Quantity, "6", ",150") == "6,150", "join: the first cell need not have kept anything");
Check(Table.Join(Field.Quantity, "4.", "670") == "4. 670",
    "join: a second cell without a separator says nothing about where the comma stood");
Check(Table.Join(Field.Quantity, "Stk", ",150") == "Stk ,150", "join: only a number continues a number");
Check(Table.Join(Field.Name, "Bratwurst grob", ",120 g") == "Bratwurst grob ,120 g",
    "join: a name is not arithmetic and keeps its space");

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

// The encoder must reproduce the vectors the weights were exported with: a drift in
// the tokenizer or the graph costs accuracy without ever crashing. Each line is
// embedded alone, as the fixture was: the int8 graph takes its activation scales per
// tensor, so the same text inside a padded batch comes out about 0.988 away. The
// fixture was measured on Apple silicon; int8 kernels are not bit-portable, so on x86
// this gate has to come down to about 0.98.
const double ParityCosine = 0.999;
using (var encoder = new Umsatzschaetzung.Suggest.Encoder())
{
    var worst = 1.0;
    var lines = 0;
    foreach (var l in File.ReadLines(Path.Combine(data, "parity.jsonl")))
    {
        if (l.Trim() == "") continue;
        using var doc = JsonDocument.Parse(l);
        var got = encoder.Embed([doc.RootElement.GetProperty("text").GetString()!])[0];
        var cos = 0.0;
        var k = 0;
        foreach (var v in doc.RootElement.GetProperty("embedding").EnumerateArray()) cos += v.GetSingle() * got[k++];
        worst = Math.Min(worst, cos);
        lines++;
    }
    Check(lines > 0 && worst >= ParityCosine, $"encoder parity: worst cosine {worst:F5} over {lines} texts");
}

async Task<List<MappingCandidate>> Suggest(string name, string unitCode) =>
    (await svc.SuggestMapping("", new InvoiceLine { Name = name, UnitCode = unitCode }, "Rheinland Getränke Fachgroßhandel GmbH", ct));

// Nothing outside the shipped ruleset is goods, so a line that is no ware may only be
// offered the kein-Wareneinsatz ingredients, of which this fixture has none.
async Task<bool> NoWare(List<MappingCandidate> sugs)
{
    var rs = await svc.Rules(ct);
    return sugs.TrueForAll(s => rs.Ingredients[s.Mapping.IngredientId].CategoryId == "cat.kein.wareneinsatz");
}

var keg = await Suggest("Fassbier Pils, Keg 50 l", "XKG");
Check(keg.Count > 0 && keg[0].Mapping.IngredientId == "ing.bier.fass" && keg[0].Mapping.Factor == 50000,
    "suggest: keg maps to the draught beer");
Check(keg[0].Kind == OriginKind.Encoder && keg[0].Confidence is > 0 and < 100, "suggest: candidate comes from the encoder and is not certain");
Check(keg[0].Mapping.Id == "", "suggest: a candidate is a proposal, not a stored mapping");

var schnaps = await Suggest("Doppelkorn 38 % vol, Flasche 0,7 l", "XBO");
Check(schnaps.Count > 0 && schnaps[0].Mapping.IngredientId == "ing.korn" && schnaps[0].Mapping.Factor == 700,
    "suggest: bottle size beats the alcohol strength");

Check(await NoWare(await Suggest("Pfand Leergut Kiste", "XCS")), "suggest: deposit has no ingredient");
var shouted = await Suggest("FASSBIER PILS, KEG 50 L", "XKG");
Check(shouted.Count > 0 && shouted[0].Mapping.IngredientId == "ing.bier.fass" && shouted[0].Confidence >= keg[0].Confidence - 5,
    $"suggest: an upper-case wording reads like the catalogue's, {keg[0].Confidence} -> {(shouted.Count > 0 ? shouted[0].Confidence : 0)}");
Check((await Suggest("Fassbier Pils, Keg 50 l", "XKG"))[0].Mapping.Factor == 50000, "suggest: index is reused");

var friseur = await svc.PutCase(new Case
{
    Label = "Salon",
    PeriodFrom = new DateOnly(2024, 1, 1),
    PeriodTo = new DateOnly(2024, 12, 31),
    Taxpayer = new Taxpayer { Name = "Schnitt", TaxNumber = "1/2", PabNumber = "3", Gewerbe = "96021.0" },
}, ct);
var line = new InvoiceLine { Name = "Doppelkorn 38 % vol, Flasche 0,7 l", UnitCode = "XBO" };
Check((await svc.SuggestMapping(friseur.Id, line, null, ct)).TrueForAll(s => s.Mapping.IngredientId != "ing.korn"),
    "suggest: a Friseur is never offered Korn");
Check((await svc.SuggestMapping(neu.Id, line, null, ct))[0].Mapping.IngredientId == "ing.korn", "suggest: a case without Gewerbe sees everything");

var parsed = await svc.ParseInvoice(kase.Id, "zugferd.pdf", File.ReadAllBytes(Path.Combine(data, "zugferd.pdf")), ct);
Check(!parsed.NeedsOcr && parsed.Invoice.Number == "RE-20201121/508" && parsed.Invoice.Lines.Count == 3, "zugferd parse");
Check(parsed.Case is { Invoices.Count: 2 } && parsed.UnmappedLines.Count == 3, "zugferd attached, lines unmapped");
try
{
    await svc.InvoiceSource(kase.Id, parsed.Invoice.Id, ct);
    Check(false, "pdf preview needs a renderer");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Unsupported, "pdf preview without renderer is Unsupported, got " + e.Code);
}

var dump = await svc.ExportCase(kase.Id, ct);
try
{
    await svc.ImportCase(dump.FileName, dump.Data, false, ct);
    Check(false, "an existing case must not be replaced unasked");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Conflict && (string?)e.Details == kase.Label,
        "re-import refused with Conflict and the existing label, got " + e.Code);
}
var back = await svc.ImportCase(dump.FileName, dump.Data, true, ct);
Check(dump.FileName.EndsWith(".db") && back.Id == kase.Id && back.Invoices.Count == 2,
    "a case exports and imports as one file");
Check(cases.LoadFile(kase.Id, parsed.Invoice.Id).Name == "zugferd.pdf", "the document travels inside it");
try
{
    await svc.ImportCase("kaputt.db", [1, 2, 3], false, ct);
    Check(false, "a file that is not a case must be refused");
}
catch (ServiceError e)
{
    Check(e.Code == ErrorCode.Invalid, "broken case file refused with Invalid, got " + e.Code);
}

var korn = (await svc.Rules(ct)).Products["prod.korn.4cl"];
korn.Meta.ValidTo = new DateOnly(2024, 6, 30);
var saved = await svc.SaveRule(korn, ct);
Check(saved.Version == 1 && saved.Products["prod.korn.4cl"].Meta.Rev == 1, "save bumps version and stamps the entity");
var alkoholfrei = new Category { Id = "cat.alkoholfrei", Name = "Alkoholfrei" };
Check((await svc.SaveRule(alkoholfrei, ct)).Categories.ContainsKey("cat.alkoholfrei"), "a category is a rule entity of its own");
var water = new Ingredient { Id = "ing.wasser", Name = "Mineralwasser", CategoryId = "cat.alkoholfrei" };
var merged = await svc.SaveRule(water, ct);
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
var pruned = await svc.DeleteRule(Entity.Product, "prod.korn.2cl", ct);
Check(pruned.Version == 4 && !pruned.Products.ContainsKey("prod.korn.2cl"), "delete drops the entity and bumps the version");

var recalced = await svc.Calculate(kase.Id, ct);
Check(recalced.Report.Products.All(p => p.ProductId != "prod.korn.4cl"), "retired product leaves the calculation");

RuleStore Reopen() => new(store, seed);
Check(!Reopen().Load().Products.ContainsKey("prod.korn.2cl"), "the seed does not resurrect a deleted entity");
Check(Directory.GetFiles(Path.Combine(store, "snapshots")).Length == 1, "second start snapshots the store");
File.WriteAllText(Path.Combine(store, "rules.db"), "kaputt");
var restored = Reopen();
Check(restored.Notice is not null && restored.Load().Version == 4, "corrupt store restored from snapshot");
Check(Directory.GetFiles(store, "rules.db.defekt-*").Length == 1, "corrupt file kept aside");

// Dynamic ranking: a confirmed mapping teaches the wording, and the wording carries
// to a line nobody has seen, with its own pack size.
var naive = await Suggest("Zwickl naturtrueb, Keg 50 l", "XKG");
var naiveFass = naive.Find(s => s.Mapping.IngredientId == "ing.bier.fass")?.Confidence ?? 0;
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
Check(learned[0].Confidence >= naiveFass && learned[0].Confidence >= 80,
    $"suggest: the confirmed wording carries, {naiveFass} -> {learned[0].Confidence}");
Check(await NoWare(await Suggest("Pfand Leergut Kiste", "XCS")), "suggest: what was learnt does not drag the deposit line along");

// Revising a mapped line: the rule it carries leads, and the encoder's alternatives
// follow — here the article number says keg, the wording says Korn.
var revising = await svc.SuggestMapping("", new InvoiceLine { Name = "Doppelkorn 38 % vol, Flasche 0,7 l", SellerArticleId = "Z-1", UnitCode = "XBO" },
    "Rheinland Getränke Fachgroßhandel GmbH", ct);
Check(revising.Count > 1 && revising[0].Kind == OriginKind.Exact && revising[0].Mapping.Id == "map.zwickl"
    && revising[1].Kind == OriginKind.Encoder && revising[1].Mapping.IngredientId == "ing.korn"
    && revising.Skip(1).All(s => s.Mapping.IngredientId != "ing.bier.fass"),
    "suggest: an exact hit leads and the encoder's alternatives follow");

// An edited line leaves a rule that no longer fits it behind.
var rheinland = new Invoice
{
    Source = Source.Ubl, SupplierName = "Rheinland Getränke Fachgroßhandel GmbH", Number = "R-Z", Date = new DateOnly(2024, 3, 1),
    Lines = [new InvoiceLine { No = 1, Name = "Zwickl naturtrueb, Keg 50 l", SellerArticleId = "Z-1", UnitCode = "XKG", Quantity = 1000, MappingId = "map.zwickl" }],
};
var stillFits = await svc.VerifyInvoice(new VerifyReq("", rheinland, Intent.Check, null, null), ct);
Check(stillFits.Invoice.Lines[0].MappingId == "map.zwickl", "verify: a line keeps the rule that still fits it");
rheinland.Lines[0].SellerArticleId = "Z-9";
var left = await svc.VerifyInvoice(new VerifyReq("", rheinland, Intent.Check, null, null), ct);
Check(string.IsNullOrEmpty(left.Invoice.Lines[0].MappingId), "verify: an edited article number leaves the rule behind");

// A case whose lines were imported before the rule existed catches up on request.
var late = await svc.PutCase(new Case
{
    Label = "Nachzügler", PeriodFrom = new DateOnly(2024, 1, 1), PeriodTo = new DateOnly(2024, 12, 31),
    Taxpayer = new Taxpayer { Name = "Späth", TaxNumber = "1/3", PabNumber = "4", Gewerbe = "56101.0" },
    Invoices = [new Invoice
    {
        Id = "re-late", Source = Source.Ubl, SupplierName = "Rheinland Getränke Fachgroßhandel GmbH", Number = "R-L", Date = new DateOnly(2024, 4, 1),
        Lines = [new InvoiceLine { No = 1, Name = "Zwickl naturtrueb, Keg 50 l", SellerArticleId = "Z-1", UnitCode = "XKG", Quantity = 1000 },
                 new InvoiceLine { No = 2, Name = "Fassbier Pils, Keg 50 l", UnitCode = "XKG", Quantity = 2000 }],
    }],
}, ct);
var caughtUp = await svc.MapCase(late.Id, ct);
Check(caughtUp.Invoices[0].Lines[0].MappingId == "map.zwickl", "map case: the rule takes the line it fits");
Check(caughtUp.Invoices[0].Lines[1].MappingId is { } lateId && (await svc.Rules(ct)).Mappings[lateId] is { Confirmed: false, IngredientId: "ing.bier.fass" },
    "map case: a sure guess maps the open line with an unconfirmed rule");
Check((await svc.GetCase(late.Id, ct)).Invoices[0].Lines[1].MappingId is not null, "map case: the catch-up is saved");

// A machine's guess is tied to the wording it was made from.
await svc.SaveRule(new ArticleMapping
{
    Id = "map.guess", SupplierName = "Rheinland Getränke Fachgroßhandel GmbH", SupplierArticleId = "G-1",
    Observed = "Pils Kiste 20 x 0,5 l", IngredientId = "ing.bier.fass", Confirmed = false,
}, ct);
var sameWording = await svc.SuggestMapping("", new InvoiceLine { Name = "Pils Kiste 20 x 0,5 l", SellerArticleId = "G-1" }, "Rheinland Getränke Fachgroßhandel GmbH", ct);
var otherWording = await svc.SuggestMapping("", new InvoiceLine { Name = "Weizen Kiste 20 x 0,5 l", SellerArticleId = "G-1" }, "Rheinland Getränke Fachgroßhandel GmbH", ct);
Check(sameWording.Count > 0 && sameWording[0].Mapping.Id == "map.guess", "match: an unconfirmed mapping fits its own wording");
Check(otherWording.TrueForAll(s => s.Mapping.Id != "map.guess"), "match: an unconfirmed mapping does not fit another wording");

var sammlungen = await svc.Sammlungen(ct);
Check(sammlungen.Count > 0 && sammlungen.TrueForAll(s => s.Mitgeliefert),
    "richtsatz: the shipped Sammlungen seed themselves");
Check(sammlungen[0].Year > sammlungen[^1].Year, "richtsatz: newest year first");
var jüngste = sammlungen[0].Year;
var nach = await svc.DeleteSammlung(jüngste, ct);
Check(nach.Count == sammlungen.Count - 1, "richtsatz: a Sammlung can be dropped");
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
    var wieder = importiert.Find(s => s.Year == jüngste);
    Check(wieder is { Mitgeliefert: false, Klassen: > 0 } && wieder.Quelle == Path.GetFileName(pdfPfad),
        "richtsatz: an imported PDF takes the place of its year");
    Check((await svc.DeleteSammlung(jüngste, ct)).TrueForAll(s => s.Year != jüngste), "richtsatz: the import can be dropped again");
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

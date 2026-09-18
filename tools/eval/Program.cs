using System.Globalization;
using Umsatzschätzung.Eval;
using Umsatzschätzung.Extract;
using Umsatzschätzung.Model;

var rows = "tools/train/page.jsonl";
var corpus = "fixtures/dataset/gen";
var split = "val";
var repair = true;
var output = "";
var detail = "";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--rows": rows = args[++i]; break;
        case "--corpus": corpus = args[++i]; break;
        case "--split": split = args[++i]; break;
        case "--no-repair": repair = false; break;
        case "--out": output = args[++i]; break;
        case "--detail": detail = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine("eval [--rows page.jsonl] [--corpus fixtures/dataset/gen] [--split val] [--no-repair] [--out datei] [--detail datei]");
            return 0;
        default:
            Console.Error.WriteLine($"unbekannte Option: {args[i]}");
            return 2;
    }
}

var results = new List<Result>();
var rowsOut = new List<string>();
foreach (var variation in Corpus.Read(rows, split, repair))
{
    var want = Corpus.Expected(Path.Combine(corpus, variation.Invoice, "expected.json"));
    var pages = variation.Pages.Select(_ => new OcrPage()).ToList();
    var got = Assemble.Invoice(variation.Pages, pages);
    var r = Score.One(Doc(got), want, VatExempt(variation, want));
    results.Add(r);
    rowsOut.Add($"{variation.Invoice}\t{variation.Template}\t{r.CellsWrong}\t{r.CellsTotal}\t{r.LinesMatched}\t{r.LinesGot}\t{r.LinesWant}\t" +
        string.Join(",", Score.HeaderFields.Select(f => r.Header[f] ? 1 : 0)));
}

var title = $"C# / {split}" + (repair ? "" : ", repair off");
var text = $"[{title}]\n" + Score.Report(results);
Console.WriteLine(text);
if (output != "") File.WriteAllText(output, text + "\n");
if (detail != "") File.WriteAllLines(detail, rowsOut.Order(StringComparer.Ordinal));
return 0;

// The rate is unanswerable only when no line prints one and the invoice carries more than
// one — which needs the genuine expected.json, since an assembled invoice has already
// inherited the single totals rate onto every line.
static bool VatExempt(Variation variation, Doc want) =>
    !variation.PerLineVat && want.Lines.Where(l => l.Vat != 0).Select(l => l.Vat).Distinct().Count() > 1;

static Doc Doc(Invoice inv) => new(
    inv.Number, inv.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", inv.SupplierName,
    inv.StatedNet ?? inv.NetTotal, inv.StatedGross ?? inv.GrossTotal,
    [.. inv.Lines.Select(l => new Line(l.Name, l.Quantity, l.UnitCode, l.UnitPrice, l.LineNet, l.Vat))]);

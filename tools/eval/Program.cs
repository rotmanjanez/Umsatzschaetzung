using System.Globalization;
using Umsatzschaetzung.Eval;
using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

var corpus = "fixtures/dataset/2025";
var rows = "";
var split = "val";
var output = "";
var detail = "";
var full = false;
var parity = false;
var show = "";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--corpus": corpus = args[++i]; break;
        case "--rows": rows = args[++i]; break;
        case "--split": split = args[++i]; break;
        case "--out": output = args[++i]; break;
        case "--detail": detail = args[++i]; break;
        case "--full": full = true; break;
        case "--parity": parity = true; break;
        case "--show": show = args[++i]; break;
        case "-h" or "--help":
            Console.WriteLine("eval [--corpus fixtures/dataset/2025] [--rows page.jsonl [--split val] [--parity]] [--full] [--out datei] [--detail datei] [--show teilname]");
            return 0;
        default:
            Console.Error.WriteLine($"unbekannte Option: {args[i]}");
            return 2;
    }
}

using var tagger = rows == "" || parity ? new Tagger() : null;
var agree = new Agreement();
var results = new List<Result>();
int accepted = 0, acceptedClean = 0;
var rowsOut = new List<string>();
foreach (var variation in rows == "" ? Corpus.ReadDumps(corpus) : Corpus.ReadRows(rows, split))
{
    var want = Corpus.Expected(Corpus.ExpectedPath(corpus, variation.Invoice));
    var tagged = variation.Pages.Select(p =>
        rows == "" ? tagger!.Tag([.. p.Words.Select(w => w.Word)], p.Width, p.Height)
        : parity ? Retag(tagger!, p, agree)
        : p.Words).ToList();
    var pages = tagged.Select(_ => new OcrPage()).ToList();
    var got = Assemble.Invoice(tagged, pages);
    (got.NetTotal, got.GrossTotal) = InvoiceMath.LineTotals(got.Lines);
    var flags = Check.Invoice(got);
    var auto = Check.Complete(got, flags);
    if (auto) accepted++;
    var r = Score.One(got, want, VatExempt(pages, want), full);
    if (auto && r.Clean) acceptedClean++;
    if (show != "" && variation.Invoice.Contains(show)) Show(variation, tagged, got, want, r);
    results.Add(r);
    rowsOut.Add($"{variation.Invoice}\t{variation.Template}\t{r.CellsWrong}\t{r.CellsTotal}\t{r.LinesMatched}\t{r.LinesGot}\t{r.LinesWant}\t" +
        string.Join(",", Score.HeaderFields.Select(f => r.Header[f] ? 1 : 0)) +
        $"\t{(auto ? "auto" : "review")}\t{string.Join(",", flags.Select(f => f.Code).Distinct().Order(StringComparer.Ordinal))}");
}

if (parity) Console.WriteLine(agree.Report() + "\n");
var title = (rows == "" ? $"{corpus}, app path" : $"{rows} / {split}" + (parity ? ", C# tagger" : "")) + (full ? ", full" : ", core");
var text = $"[{title}]\n" + Score.Report(results, full)
    + $"\nauto accept         {(results.Count == 0 ? 0 : accepted / (double)results.Count):F3}  ({accepted}, davon {acceptedClean} fehlerfrei)";
Console.WriteLine(text);
if (output != "") File.WriteAllText(output, text + "\n");
if (detail != "") File.WriteAllLines(detail, rowsOut.Order(StringComparer.Ordinal));
return 0;

static void Show(Variation variation, List<List<TaggedWord>> tagged, Invoice got, Invoice want, Result r)
{
    Console.WriteLine($"## {variation.Invoice} / {variation.Template}  wrong {r.CellsWrong}/{r.CellsTotal}  header {string.Join(",", Score.HeaderFields.Select(f => r.Header[f] ? 1 : 0))}");
    Table? layout = null;
    foreach (var page in tagged)
    {
        var table = Table.Read(page, layout);
        if (table.HasColumns) layout = table;
        Console.WriteLine(table.Describe());
        Console.WriteLine(table.DescribeRows(page));
    }
    Console.WriteLine($"  got  {got.Number} | {got.Date} | {got.SupplierName} | {got.StatedNet} | {got.StatedGross}");
    Console.WriteLine($"  want {want.Number} | {want.Date} | {want.SupplierName} | {want.NetTotal} | {want.GrossTotal}");
    var pairs = Score.Match(got.Lines, want.Lines).ToDictionary(p => p.Got, p => p.Want);
    for (var i = 0; i < got.Lines.Count; i++)
    {
        Console.WriteLine($"  got  {L(got.Lines[i])}");
        if (pairs.TryGetValue(i, out var j)) Console.WriteLine($"  want {L(want.Lines[j])}" + (L(want.Lines[j]) == L(got.Lines[i]) ? "  ok" : "  <-- DIFF"));
        else Console.WriteLine("  want (unmatched)");
    }
    foreach (var j in Enumerable.Range(0, want.Lines.Count).Except(pairs.Values))
        Console.WriteLine($"  miss {L(want.Lines[j])}");
    static string L(InvoiceLine l) => $"{l.Quantity} {l.UnitCode} | {l.Name} | {l.UnitPrice} | {l.LineNet} | {l.Vat}";
}

// The dump's rows go in unchanged so every word lines up with its Python prediction.
static List<TaggedWord> Retag(Tagger tagger, Page page, Agreement agree)
{
    var got = tagger.Tag([.. page.Words.Select(w => (w.Word, w.Row))], page.Width, page.Height);
    agree.Add(page.Words, got);
    return got;
}

// The rate is unanswerable only when no line prints one and the invoice carries more than one.
static bool VatExempt(List<OcrPage> pages, Invoice want) =>
    !pages.Any(p => p.Lines.Any(l => l.Cells.ContainsKey(Field.Vat)))
    && want.Lines.Where(l => l.Vat != 0).Select(l => l.Vat).Distinct().Count() > 1;

// Word-for-word agreement of the C# tagger with the dump it re-tags.
sealed class Agreement
{
    int words, rows, field, role, col, cell, lost;
    readonly List<string> examples = [];

    public void Add(List<TaggedWord> want, List<TaggedWord> got)
    {
        lost += Math.Abs(want.Count - got.Count);
        for (var i = 0; i < Math.Min(want.Count, got.Count); i++)
        {
            var (a, b) = (want[i], got[i]);
            words++;
            if (a.Field == b.Field) field++;
            if (i == 0 || want[i - 1].Row != a.Row) { rows++; if (a.Role == b.Role) role++; }
            if (a.Col == b.Col) col++;
            if (a.CellStart == b.CellStart) cell++;
            if ((a.Field != b.Field || a.Col != b.Col || a.CellStart != b.CellStart) && examples.Count < 20)
                examples.Add($"  '{a.Word.Text}' python {a.Field?.ToString() ?? "O"}/{a.Role}/{a.Col}/{(a.CellStart ? 1 : 0)}  csharp {b.Field?.ToString() ?? "O"}/{b.Role}/{b.Col}/{(b.CellStart ? 1 : 0)}");
        }
    }

    public string Report() =>
        $"[parity] words {words}, unmatched {lost}\n  field      {F(field)}\n  role       {(rows == 0 ? 0 : role / (double)rows).ToString("F4", CultureInfo.InvariantCulture)} ({rows} rows)\n  col        {F(col)}\n  cellStart  {F(cell)}\n" +
        string.Join("\n", examples);

    string F(int n) => (words == 0 ? 0 : n / (double)words).ToString("F4", CultureInfo.InvariantCulture);
}

using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Model;

public enum Source
{
    [JsonStringEnumMemberName("ubl")] Ubl,
    [JsonStringEnumMemberName("cii")] Cii,
    [JsonStringEnumMemberName("zugferd")] Zugferd,
    [JsonStringEnumMemberName("scan")] Scan,
}

public sealed class Invoice
{
    public string Id { get; set; } = "";
    public Source Source { get; set; }
    public string FileName { get; set; } = "";
    public string SupplierName { get; set; } = "";
    public string Number { get; set; } = "";
    public DateOnly? Date { get; set; }
    public string Currency { get; set; } = "";
    public long NetTotal { get; set; }
    public long GrossTotal { get; set; }
    // What the document prints; the totals above are the sum of the positions.
    public long? StatedNet { get; set; }
    public long? StatedGross { get; set; }
    public List<InvoiceLine> Lines { get; set; } = [];
    public Verification? Verification { get; set; }
}

public sealed class InvoiceLine
{
    public long No { get; set; }
    public string Name { get; set; } = "";
    public string? SellerArticleId { get; set; }
    public string? Gtin { get; set; }
    public long Quantity { get; set; }
    public string UnitCode { get; set; } = "";
    public long UnitPrice { get; set; }
    public long PriceBaseQty { get; set; }
    public long LineNet { get; set; }
    public long Vat { get; set; }
    public string? MappingId { get; set; }
}

// Die Rechnungszeilen, die in die Kalkulation eingegangen sind: ohne die Zeilen ohne
// Zuordnung, die, deren Zutat in keiner Rezeptur vorkommt, und Pfand und Leergut.
public sealed record Included(string Number, string FileName, DateOnly? Date, List<InvoiceLine> Lines)
{
    public static List<Included> Of(Case c, Report r)
    {
        HashSet<(string, long)> excluded = [];
        foreach (var l in r.Unmapped) excluded.Add((l.InvoiceId, l.LineNo));
        foreach (var l in r.Unused.Concat(r.Deposits).Concat(r.NoRevenue)) excluded.Add((l.InvoiceId, l.LineNo));
        List<Included> output = [];
        foreach (var inv in c.Invoices.OrderBy(i => i.Date).ThenBy(i => i.Number, StringComparer.Ordinal))
        {
            var lines = inv.Lines.FindAll(l => !excluded.Contains((inv.Id, l.No)));
            if (lines.Count > 0) output.Add(new Included(inv.Number, inv.FileName, inv.Date, lines));
        }
        return output;
    }
}

public sealed class Verification
{
    public DateTimeOffset At { get; set; }
    public bool Auto { get; set; }
}

public enum Field
{
    [JsonStringEnumMemberName("quantity")] Quantity,
    [JsonStringEnumMemberName("unit")] Unit,
    [JsonStringEnumMemberName("name")] Name,
    [JsonStringEnumMemberName("articleId")] ArticleId,
    [JsonStringEnumMemberName("unitPrice")] UnitPrice,
    [JsonStringEnumMemberName("lineNet")] LineNet,
    [JsonStringEnumMemberName("vat")] Vat,
    [JsonStringEnumMemberName("invoiceNumber")] InvoiceNumber,
    [JsonStringEnumMemberName("invoiceDate")] InvoiceDate,
    [JsonStringEnumMemberName("supplier")] Supplier,
    [JsonStringEnumMemberName("netTotal")] NetTotal,
    [JsonStringEnumMemberName("grossTotal")] GrossTotal,

    // In the order of FIELDS in tools/train/schema.py.
    [JsonStringEnumMemberName("numberLabel")] NumberLabel,
    [JsonStringEnumMemberName("dateLabel")] DateLabel,
    [JsonStringEnumMemberName("netLabel")] NetLabel,
    [JsonStringEnumMemberName("grossLabel")] GrossLabel,
    [JsonStringEnumMemberName("vatLabel")] VatLabel,
    [JsonStringEnumMemberName("otherLabel")] OtherLabel,

    // A word inside the item table; its column decides the field, in Extract/Table.cs.
    [JsonStringEnumMemberName("cell")] Cell,
}

public sealed record Box(int X, int Y, int W, int H);

public sealed class OcrWord
{
    public string Text { get; set; } = "";
    public Box Box { get; set; } = new(0, 0, 0, 0);
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public float Confidence { get; set; }
}

public sealed class Flag
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long LineNo { get; set; }
    public Field? Field { get; set; }
}

public sealed class OcrLine
{
    public Dictionary<Field, OcrWord> Cells { get; set; } = [];
    public InvoiceLine Parsed { get; set; } = new();
    public List<Flag> Flags { get; set; } = [];
}

// How a page was brought upright before it was read, in order: scaled, straightened, turned
// and straightened again. A fresh render of the document replays it to sit under the boxes.
public sealed class Correction
{
    public double Scale { get; set; } = 1;
    public double Skew { get; set; }
    public int Turn { get; set; }
    public double Settle { get; set; }
}

// Pixels as the screen takes them: BGRA, premultiplied, rows back to back. Nothing is encoded
// between the renderer and the view.
public sealed record Raster(int Width, int Height, byte[] Pixels);

public sealed class OcrPage
{
    public Raster? Image { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public Correction Correction { get; set; } = new();
    public List<OcrWord> Words { get; set; } = [];
    public Dictionary<Field, OcrWord> Header { get; set; } = [];
    public List<OcrLine> Lines { get; set; } = [];
    public List<Flag> Flags { get; set; } = [];
}

public static class InvoiceMath
{
    public static (long Net, long Gross) LineTotals(IEnumerable<InvoiceLine> lines)
    {
        long net = 0;
        var vatBase = new Dictionary<long, long>();
        foreach (var l in lines)
        {
            net += l.LineNet;
            vatBase[l.Vat] = vatBase.GetValueOrDefault(l.Vat) + l.LineNet;
        }
        long gross = net;
        foreach (var (rate, b) in vatBase)
            gross += RoundDiv(b * rate, Bp.Full);
        return (net, gross);
    }

    // Quantity in milli, unit price in micro per PriceBaseQty milli, net in cents.
    public static long LineNet(long quantity, long unitPrice, long priceBaseQty) =>
        RoundDiv(quantity * unitPrice, (priceBaseQty > 0 ? priceBaseQty : 1000) * 10000);

    public static long RoundDiv(long num, long den) =>
        den < 0 ? RoundDiv(-num, -den)
        : num < 0 ? -((-num + den / 2) / den) : (num + den / 2) / den;
}

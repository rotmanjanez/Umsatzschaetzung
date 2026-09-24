using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Extract;

public static class Check
{
    // Line nets and line sums reproduce the document's own integer arithmetic to the cent. The
    // gross total does not: suppliers either apply each rate once to its share of the net or round
    // the tax per position and add those up, which differ by a cent on half-way lines. Both count.
    // A mismatch sits on every cell of its formula, since any of them may be the misread one.
    public static List<Flag> Invoice(Invoice inv)
    {
        var flags = new List<Flag>();
        var net = inv.StatedNet ?? inv.NetTotal;
        var gross = inv.StatedGross ?? inv.GrossTotal;
        long sum = 0, lineTax = 0;
        var bases = new Dictionary<long, long>();
        foreach (var l in inv.Lines)
        {
            sum += l.LineNet;
            bases[l.Vat] = bases.GetValueOrDefault(l.Vat) + l.LineNet;
            lineTax += InvoiceMath.RoundDiv(l.LineNet * l.Vat, Bp.Full);
            if (l.Quantity == 0)
                flags.Add(new Flag { Code = "zero", LineNo = l.No, Field = Field.Quantity, Message = $"Zeile {l.No}: Menge ist null" });
            if (l.UnitCode == "")
                flags.Add(new Flag { Code = "no_unit", LineNo = l.No, Field = Field.Unit, Message = $"Zeile {l.No}: Einheit fehlt" });
            var expected = InvoiceMath.LineNet(l.Quantity, l.UnitPrice, l.PriceBaseQty);
            if (expected != l.LineNet)
                flags.AddRange(Cells(
                    "line_total",
                    $"Zeile {l.No}: Menge × Einzelpreis ergibt {Format.Cents(expected)}, Gesamtpreis ist {Format.Cents(l.LineNet)}",
                    [(l.No, Field.Quantity), (l.No, Field.UnitPrice), (l.No, Field.LineNet)]));
        }
        if (net <= 0)
            flags.Add(new Flag { Code = "nonpositive", Field = Field.NetTotal, Message = "Nettobetrag ist nicht positiv" });
        if (gross <= 0)
            flags.Add(new Flag { Code = "nonpositive", Field = Field.GrossTotal, Message = "Bruttobetrag ist nicht positiv" });
        if (inv.Lines.Count > 0 && sum != net)
            flags.AddRange(Cells(
                "sum_net",
                $"Summe der Positionen {Format.Cents(sum)} weicht vom Nettobetrag {Format.Cents(net)} ab",
                [(0, Field.NetTotal), .. inv.Lines.Select(l => (l.No, Field.LineNet))]));
        if (inv.Lines.Count > 0)
        {
            var single = bases.Count == 1 ? bases.Keys.Single() : (long?)null;
            var expected = net + (single is { } vat
                ? InvoiceMath.RoundDiv(net * vat, Bp.Full)
                : bases.Sum(b => InvoiceMath.RoundDiv(b.Value * b.Key, Bp.Full)));
            if (expected != gross && net + lineTax != gross)
                flags.AddRange(Cells(
                    "gross_check",
                    $"Netto {Format.Cents(net)} zzgl. {(single is { } rate ? Format.Bp(rate) + " " : "")}MwSt ergibt {Format.Cents(expected)}, Bruttobetrag ist {Format.Cents(gross)}",
                    [(0, Field.GrossTotal), (0, Field.NetTotal), .. inv.Lines.Select(l => (l.No, Field.Vat))]));
        }
        return flags;
    }

    static IEnumerable<Flag> Cells(string code, string message, List<(long LineNo, Field Field)> cells) =>
        cells.Select(c => new Flag { Code = code, Message = message, LineNo = c.LineNo, Field = c.Field });

    // Taken over without a human means nobody ever looks at it: that needs a complete reading
    // whose positions add up to the totals the document itself prints.
    public static bool Complete(Invoice inv, List<Flag> flags) =>
        flags.Count == 0
        && inv.Lines.Count > 0
        && inv.SupplierName != ""
        && inv.Number != ""
        && inv.Date is not null
        && inv.StatedNet is > 0
        && inv.StatedGross is > 0;

    // A mismatch against a printed total is the reader's word against the document, so it only
    // stops the automatic route; totals the invoice carries itself are binding.
    public static bool Blocks(Invoice inv, Flag flag) =>
        flag.Code == "line_total" || (flag.Code == "sum_net" && inv.StatedNet is null);
}

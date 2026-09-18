using Umsatzschätzung.Model;

namespace Umsatzschätzung.Extract;

public static class Check
{
    public static List<Flag> Invoice(Invoice inv)
    {
        // Number, date and supplier are nice to have: only the positions and the totals
        // decide whether an invoice adds up.
        var flags = new List<Flag>();
        var net = inv.StatedNet ?? inv.NetTotal;
        var gross = inv.StatedGross ?? inv.GrossTotal;
        long sum = 0;
        long vat = 0;
        var singleVat = inv.Lines.Count > 0;
        foreach (var l in inv.Lines)
        {
            sum += l.LineNet;
            if (l.Quantity <= 0)
                flags.Add(new Flag { Code = "nonpositive", LineNo = l.No, Field = Field.Quantity, Message = $"Zeile {l.No}: Menge ist nicht positiv" });
            if (l.UnitPrice <= 0)
                flags.Add(new Flag { Code = "nonpositive", LineNo = l.No, Field = Field.UnitPrice, Message = $"Zeile {l.No}: Einzelpreis ist nicht positiv" });
            if (l.LineNet <= 0)
                flags.Add(new Flag { Code = "nonpositive", LineNo = l.No, Field = Field.LineNet, Message = $"Zeile {l.No}: Gesamtpreis ist nicht positiv" });
            var expected = ExpectedLineNet(l);
            if (!Within(expected, l.LineNet))
                flags.Add(new Flag
                {
                    Code = "line_total",
                    LineNo = l.No,
                    Field = Field.LineNet,
                    Message = $"Zeile {l.No}: Menge × Einzelpreis ergibt {Format.Cents(expected)}, Gesamtpreis ist {Format.Cents(l.LineNet)}",
                });
            if (vat == 0) vat = l.Vat;
            else if (l.Vat != vat) singleVat = false;
        }
        if (net <= 0)
            flags.Add(new Flag { Code = "nonpositive", Field = Field.NetTotal, Message = "Nettobetrag ist nicht positiv" });
        if (gross <= 0)
            flags.Add(new Flag { Code = "nonpositive", Field = Field.GrossTotal, Message = "Bruttobetrag ist nicht positiv" });
        if (inv.Lines.Count > 0 && !Within(sum, net))
            flags.Add(new Flag
            {
                Code = "sum_net",
                Field = Field.NetTotal,
                Message = $"Summe der Positionen {Format.Cents(sum)} weicht vom Nettobetrag {Format.Cents(net)} ab",
            });
        if (singleVat && vat > 0)
        {
            var expected = InvoiceMath.RoundDiv(net * (Bp.Full + vat), Bp.Full);
            if (!Within(expected, gross))
                flags.Add(new Flag
                {
                    Code = "gross_check",
                    Field = Field.GrossTotal,
                    Message = $"Netto {Format.Cents(net)} zzgl. {Format.Bp(vat)} MwSt ergibt {Format.Cents(expected)}, Bruttobetrag ist {Format.Cents(gross)}",
                });
        }
        return flags;
    }

    // Taking an invoice over without a human means nobody ever looks at it: that needs a complete
    // reading whose positions add up to the totals the document itself prints.
    public static bool Complete(Invoice inv, List<Flag> flags) =>
        flags.Count == 0
        && inv.Lines.Count > 0
        && inv.SupplierName != ""
        && inv.Number != ""
        && inv.Date is not null
        && inv.StatedNet is > 0
        && inv.StatedGross is > 0;

    // A mismatch against a printed total is the reader's word against the document, so it only
    // stops the automatic route; totals that an invoice carries itself are binding.
    public static bool Blocks(Invoice inv, Flag flag) =>
        flag.Code == "line_total" || (flag.Code == "sum_net" && inv.StatedNet is null);

    static long ExpectedLineNet(InvoiceLine l)
    {
        var b = l.PriceBaseQty <= 0 ? 1000 : l.PriceBaseQty;
        return InvoiceMath.RoundDiv(l.Quantity * l.UnitPrice, b * 10000);
    }

    static bool Within(long expected, long actual)
    {
        var diff = Math.Abs(expected - actual);
        var reference = Math.Max(Math.Abs(expected), Math.Abs(actual));
        return diff <= 1 + reference / 200;
    }
}

using System.Globalization;
using System.Text;
using Avalonia.Data.Converters;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

sealed class Formatter(Func<object?, object> format) : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => format(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

// Der Rechenweg in Worten: die Schritte stehen als Zahlen im Bericht, die Sätze dazu hier.
public static class Fmt
{
    public static readonly IValueConverter Cents = new Formatter(v => v is long c ? Format.Cents(c) : "");

    public static readonly IValueConverter Portions = new Formatter(v => v is long p ? Format.Portions(p) : "");

    public static readonly IValueConverter Sellable = Of<IngredientRow>(i => Format.Qty(i.Sellable, i.Unit));
    public static readonly IValueConverter SellableNote = Of<IngredientRow>(Yield);
    public static readonly IValueConverter Used = Of<IngredientRow>(i => Format.Qty(i.Used, i.Unit));
    public static readonly IValueConverter UsedNote = Of<IngredientRow>(Consumption);
    public static readonly IValueConverter Bought = Of<IngredientRow>(i => Format.Qty(i.Bought, i.Unit));
    public static readonly IValueConverter BoughtNote = Of<IngredientRow>(i => "netto " + Format.Cents(i.Cost));
    public static readonly IValueConverter HasYield = Flag<IngredientRow>(i => i.Yield is not null);
    public static readonly IValueConverter HasStock = Flag<IngredientRow>(i => i.Opening != 0 || i.Closing != 0);

    public static readonly IValueConverter Line = Of<Purchase>(p => $"Rechnung {p.Invoice} Pos. {p.LineNo}: {p.Name}");
    public static readonly IValueConverter Qty = Of<Purchase>(p => Format.Qty(p.Qty, p.Unit));
    public static readonly IValueConverter QtyNote = Of<Purchase>(p => Format.Quantity(p.Quantity, p.UnitCode)
        + (p.Factor > 0 ? " × " + Format.Qty(p.Factor, p.Unit) : "")
        + $" = {Format.Qty(p.Qty, p.Unit)} (netto {Format.Cents(p.Net)})");

    public static readonly IValueConverter Sold = Of<ProductRow>(p => p.Name + ": Umsatz (netto)");
    public static readonly IValueConverter SoldNote = Of<ProductRow>(p => p.PriceMissing
        ? $"{Format.Portions(p.Portions)} × Preis fehlt"
        : $"{Format.Cents(p.GrossPrice)} brutto ÷ (100 % + {Format.Bp(p.Vat)} USt) = {Format.Cents(p.UnitNet)} netto je Portion; "
            + $"{Format.Portions(p.Portions)} × {Format.Cents(p.UnitNet)} = {Format.Cents(p.RevenueNet)}");
    public static readonly IValueConverter PortionsNote = Of<ProductRow>(p => p.Pinned
        ? "vorgegeben" + (p.PinReason == "" ? "" : ": " + p.PinReason)
        : "Zuteilung" + (p.Binding.Count > 0 ? ", begrenzt durch " + string.Join(", ", p.Binding) : ""));
    public static readonly IValueConverter PriceNote = Of<ProductRow>(p =>
        p.PriceMissing ? "Preis fehlt" : $"{Format.Cents(p.GrossPrice)} brutto, {Format.Bp(p.Vat)} USt");

    static IValueConverter Of<T>(Func<T, string> text) => new Formatter(v => v is T x ? text(x) : "");

    static IValueConverter Flag<T>(Func<T, bool> holds) => new Formatter(v => v is T x && holds(x));

    static string Yield(IngredientRow i)
    {
        if (i.Yield is not { } y) return "";
        var b = new StringBuilder(Format.Qty(i.Used, i.Unit) + " × (100 %");
        foreach (var (rate, label) in new[]
        {
            (y.Shrinkage, i.Unit == Unit.Ml ? "Schankverlust" : "Schwund"),
            (y.OwnUse, "Eigenverbrauch"), (y.Staff, "Personalverzehr"), (y.Free, "Freiabgabe"),
        })
            if (rate != 0) b.Append(" − ").Append(Format.Bp(rate)).Append(' ').Append(label);
        b.Append(") = ").Append(Format.Qty(i.Sellable, i.Unit)).Append(" · Ertragsregel „").Append(y.Name).Append('“');
        return b + (i.YieldChosen ? ", in der Prüfung gewählt" : "");
    }

    static string Consumption(IngredientRow i) =>
        i.Opening == 0 && i.Closing == 0 ? ""
            : $"Anfangsbestand {Format.Qty(i.Opening, i.Unit)} + Einkauf {Format.Qty(i.Bought, i.Unit)} "
                + $"− Endbestand {Format.Qty(i.Closing, i.Unit)} = {Format.Qty(i.Used, i.Unit)}";
}

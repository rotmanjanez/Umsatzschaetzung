using System.Text.Json.Serialization;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Model;

public enum FactorSource
{
    [JsonStringEnumMemberName("table")] Table,
    [JsonStringEnumMemberName("manual")] Manual,
    [JsonStringEnumMemberName("pack")] Pack,
    [JsonStringEnumMemberName("piece")] Piece,
}

// Wie viel Rezepteinheit in einer verrechneten Einheit steckt: Rezeptmenge = Menge × Faktor / (Per × 1000).
// Per ist nur beim Stückzählen über ein Gewicht nicht 1 -- 1 kg Gurken sind 1.000 / 400 Stück, gerundet
// wird erst die ganze Zeile. Die Einheitentabelle rechnet ohne Faktor (0), sonst gilt der Faktor der
// Zuordnung, dann die Packungsangabe der Bezeichnung, zuletzt der Stück-Richtwert der Zutat -- ein Schätzwert.
public static class Factors
{
    public static (long Factor, long Per, FactorSource Source)? Of(RuleSet rs, ArticleMapping m, InvoiceLine line) =>
        Of(rs, m.IngredientId, line, m.Factor);

    public static (long Factor, long Per, FactorSource Source)? Of(RuleSet rs, string ingredientId, InvoiceLine line, long? mappingFactor) =>
        Of(Scale.Of(rs, ingredientId), rs.Ingredients.GetValueOrDefault(ingredientId)?.Piece, line.UnitCode, PackSize.Read(line.Name), mappingFactor);

    public static (long Factor, long Per, FactorSource Source)? Of(Unit? recipeUnit, Piece? piece, string unitCode, Pack? pack, long? mappingFactor)
    {
        if (recipeUnit is not { } unit) return null;
        var billed = Units.Lookup(unitCode);
        if (billed is { Container: false } t && t.Base == unit) return (0, 1, FactorSource.Table);
        if (mappingFactor is { } manual) return (manual, 1, FactorSource.Manual);
        if (piece is not { Amount: > 0, Unit: Unit.G or Unit.Ml }) piece = null;
        if (pack is { } p)
        {
            if (unit == Unit.Piece && p.Base == Unit.Piece) return (p.Count, 1, FactorSource.Pack);
            if (unit != Unit.Piece && (p.Base == unit || p.Base is null)) return (p.Count * p.Size, 1, FactorSource.Pack);
            if (Weighed(unit, piece, p.Base, p.Base == Unit.Piece ? p.Count : p.Count * p.Size) is { } w) return w;
        }
        return billed is { Container: false } u ? Weighed(unit, piece, u.Base, u.Factor) : null;
    }

    // Eine Menge in der Einheit "billed" über den Stück-Richtwert in der Rezepteinheit: Stück mal
    // Gewicht, oder Gewicht durch Gewicht je Stück. Gramm und Milliliter mischt der Richtwert nie.
    static (long, long, FactorSource)? Weighed(Unit unit, Piece? piece, Unit? billed, long amount)
    {
        if (piece is null) return null;
        if (unit == piece.Unit && billed == Unit.Piece) return (amount * piece.Amount, 1, FactorSource.Piece);
        if (unit == Unit.Piece && (billed == piece.Unit || billed is null)) return (amount, piece.Amount, FactorSource.Piece);
        return null;
    }

    // Rezeptmenge einer Zeile; die Tabelle rechnet mit dem Faktor ihrer Einheit. Nur ein Bruch
    // wird kaufmännisch gerundet, sonst bleibt es beim Abschneiden wie bisher.
    public static long Qty(long quantity, string unitCode, (long Factor, long Per, FactorSource Source) f)
    {
        var scale = f.Source == FactorSource.Table ? Units.Lookup(unitCode)!.Factor : f.Factor;
        if (f.Per == 1) return quantity * scale / 1000;
        var d = f.Per * 1000;
        return Math.Sign(quantity) * ((Math.Abs(quantity) * scale + d / 2) / d);
    }
}

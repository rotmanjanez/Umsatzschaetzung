using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public class FactorTests
{
    static readonly Piece Gurke = new(400, Unit.G);

    static (long, long, FactorSource)? Of(long count, long size, Unit? packBase, string unitCode, Unit? recipe, Piece? piece = null, long? manual = null) =>
        Factors.Of(recipe, piece, unitCode, count == 0 ? null : new Pack(count, size, packBase), manual);

    static long? Packed(long count, long size, Unit? packBase, string unitCode, Unit? recipe) =>
        Of(count, size, packBase, unitCode, recipe) is (var f, _, FactorSource.Pack) ? f : null;

    [Theory]
    [InlineData(20, 500, Unit.Ml, "XCS", Unit.Ml, 10000L)]
    [InlineData(1, 50000, Unit.Ml, "XKG", Unit.Ml, 50000L)]
    [InlineData(1, 25000, Unit.G, "XSA", Unit.G, 25000L)]
    [InlineData(24, 330, null, "XCS", Unit.Ml, 7920L)]
    [InlineData(24, 330, null, "XCS", Unit.G, 7920L)]
    [InlineData(250, 1000, Unit.Piece, "XPK", Unit.Piece, 250L)]
    [InlineData(20, 500, Unit.Ml, "", Unit.Ml, 10000L)]
    public void AContainerIsOnlyKnownFromItsPackSize(long count, long size, Unit? packBase, string unitCode, Unit? recipe, long factor) =>
        Assert.Equal(factor, Packed(count, size, packBase, unitCode, recipe));

    [Theory]
    [InlineData(0, 0, null, "KGM", Unit.G)]
    [InlineData(1, 25000, Unit.G, "KGM", Unit.G)]
    [InlineData(1, 750, Unit.Ml, "LTR", Unit.Ml)]
    [InlineData(1, 750, Unit.Ml, "MLT", Unit.Ml)]
    [InlineData(10, 1000, Unit.Piece, "H87", Unit.Piece)]
    public void AUnitTheTableConvertsNeedsNone(long count, long size, Unit? packBase, string unitCode, Unit? recipe) =>
        Assert.Equal((0L, 1L, FactorSource.Table), Of(count, size, packBase, unitCode, recipe, Gurke, 7));

    [Theory]
    [InlineData(1, 750, Unit.Ml, "XBO", Unit.G)]
    [InlineData(1, 25000, Unit.G, "XSA", Unit.Ml)]
    [InlineData(6, 330, Unit.Ml, "XCS", Unit.Piece)]
    [InlineData(6, 330, null, "XCS", Unit.Piece)]
    [InlineData(250, 1000, Unit.Piece, "XPK", Unit.G)]
    public void ASizeInAnotherBaseUnitIsRejected(long count, long size, Unit? packBase, string unitCode, Unit? recipe) =>
        Assert.Null(Of(count, size, packBase, unitCode, recipe));

    [Theory]
    [InlineData("XCS", Unit.Ml)]
    [InlineData("KGM", Unit.Ml)]
    [InlineData("", Unit.G)]
    [InlineData("H87", Unit.G)]
    public void WithoutAPackSizeOnlyAHumanKnowsTheFactor(string unitCode, Unit recipe) =>
        Assert.Null(Of(0, 0, null, unitCode, recipe));

    [Fact]
    public void AnIngredientInNoRecipeHasNoUnitToConvertTo() =>
        Assert.Null(Of(20, 500, Unit.Ml, "XCS", null, Gurke, 7));

    [Fact]
    public void AHumansFactorGoesBeforeThePackSizeAndThePieceWeight()
    {
        Assert.Equal((7L, 1L, FactorSource.Manual), Of(20, 500, Unit.G, "XCS", Unit.G, Gurke, 7));
        Assert.Equal((7L, 1L, FactorSource.Manual), Of(0, 0, null, "H87", Unit.G, Gurke, 7));
    }

    [Fact]
    public void ThePackSizeGoesBeforeThePieceWeight() =>
        Assert.Equal((350L, 1L, FactorSource.Pack), Of(1, 350, Unit.G, "H87", Unit.G, Gurke));

    [Theory]
    [InlineData("H87", 400L)]
    [InlineData("C62", 400L)]
    [InlineData("PCE", 400L)]
    [InlineData("EA", 400L)]
    public void APieceIsWeighedByTheIngredient(string unitCode, long factor) =>
        Assert.Equal((factor, 1L, FactorSource.Piece), Of(0, 0, null, unitCode, Unit.G, Gurke));

    [Theory]
    [InlineData("XCS")]
    [InlineData("XBO")]
    public void AContainerIsNoPiece(string unitCode) =>
        Assert.Null(Of(0, 0, null, unitCode, Unit.G, Gurke));

    [Fact]
    public void ACountOnlyPackIsWeighedPieceByPiece() =>
        Assert.Equal((4800L, 1L, FactorSource.Piece), Of(12, 1000, Unit.Piece, "XCS", Unit.G, Gurke));

    [Theory]
    [InlineData(0, 0, null, "H87", Unit.Ml)]
    [InlineData(12, 1000, Unit.Piece, "XCS", Unit.Ml)]
    [InlineData(0, 0, null, "LTR", Unit.Piece)]
    public void GramsNeverStandInForMillilitres(long count, long size, Unit? packBase, string unitCode, Unit recipe) =>
        Assert.Null(Of(count, size, packBase, unitCode, recipe, Gurke));

    [Theory]
    [InlineData(0, 0, null, "KGM", 1000L)]
    [InlineData(0, 0, null, "GRM", 1L)]
    [InlineData(1, 5000, Unit.G, "XCS", 5000L)]
    [InlineData(1, 5000, null, "XCS", 5000L)]
    [InlineData(1, 5000, Unit.Ml, "XCS", null)]
    public void AWeightCountsPiecesWhereTheRecipeDoes(long count, long size, Unit? packBase, string unitCode, long? factor) =>
        Assert.Equal(factor is { } f ? (f, 400L, FactorSource.Piece) : null, Of(count, size, packBase, unitCode, Unit.Piece, Gurke));

    [Theory]
    [InlineData(10_000, "KGM", 25)]
    [InlineData(2_500, "KGM", 6)]
    [InlineData(800_000, "GRM", 2)]
    [InlineData(1_000, "KGM", 3)]
    [InlineData(100_000, "GRM", 0)]
    [InlineData(-2_500, "KGM", -6)]
    public void PiecesFromAWeightAreRoundedOnceForTheWholeLine(long quantity, string unitCode, long pieces) =>
        Assert.Equal(pieces, Factors.Qty(quantity, unitCode, Of(0, 0, null, unitCode, Unit.Piece, Gurke)!.Value));

    [Theory]
    [InlineData(2_500, "KGM", 2_500)]
    [InlineData(2_500, "H87", 1_000)]
    [InlineData(1_999, "H87", 799)]
    public void AWholeFactorKeepsCuttingOff(long quantity, string unitCode, long qty) =>
        Assert.Equal(qty, Factors.Qty(quantity, unitCode, Of(0, 0, null, unitCode, Unit.G, Gurke)!.Value));

    [Theory]
    [InlineData(0L, Unit.G)]
    [InlineData(400L, Unit.Piece)]
    public void ANonsensePieceIsIgnored(long amount, Unit unit) =>
        Assert.Null(Of(0, 0, null, "H87", Unit.G, new Piece(amount, unit)));

    static RuleSet Rules(Piece? piece)
    {
        var rs = new RuleSet();
        rs.Put(new Ingredient { Id = "ing.gurke", Name = "Gurken", Piece = piece });
        rs.Put(new Product { Id = "p", Name = "Salat", Recipe = [new() { IngredientId = "ing.gurke", Amount = 80, Unit = "GRM" }] });
        return rs;
    }

    [Fact]
    public void AKnownPieceWeightLeavesNothingToAsk()
    {
        var line = new InvoiceLine { Name = "Salatgurke Kl. I", UnitCode = "H87", Quantity = 1000 };
        Assert.True(Scale.NeedsFactor(Rules(null), "ing.gurke", line));
        Assert.False(Scale.NeedsFactor(Rules(Gurke), "ing.gurke", line));
        Assert.Equal((400L, 1L, FactorSource.Piece), Factors.Of(Rules(Gurke), new ArticleMapping { IngredientId = "ing.gurke" }, line));
        Assert.False(Scale.NeedsFactor(Rules(null), "ing.gurke", new InvoiceLine { Name = "Gurken Kiste 12 x 350 g", UnitCode = "XCS" }));
        Assert.False(Scale.NeedsFactor(Rules(null), "ing.unbekannt", line));
    }
}

public class WordingTests
{
    [Theory]
    [InlineData("FASSBIER PILS, KEG 50 L", "Fassbier Pils, Keg 50 L")]
    [InlineData("DOPPELKORN 38% VOL", "Doppelkorn 38% Vol")]
    [InlineData("MÜLLER-THURGAU Q.B.A.", "Müller-Thurgau Q.B.A.")]
    [InlineData("Fassbier PILS", "Fassbier PILS")]
    [InlineData("fassbier pils", "fassbier pils")]
    [InlineData("0,5 L", "0,5 L")]
    [InlineData("123", "123")]
    [InlineData("", "")]
    public void OnlyAWordingWithoutLowerCaseIsReadInTitleCase(string text, string normal) =>
        Assert.Equal(normal, Matcher.Normal(text));

    [Theory]
    [InlineData("Beobachtet", "Name", "Beobachtet")]
    [InlineData("", "Name", "Name")]
    [InlineData(null, "Name", "Name")]
    [InlineData(null, "", null)]
    [InlineData(null, null, null)]
    public void AMappingIsKnownByTheWordingItWasMadeFromBeforeItsName(string? observed, string? name, string? wording) =>
        Assert.Equal(wording, Matcher.Wording(new ArticleMapping { Observed = observed, Name = name }));
}

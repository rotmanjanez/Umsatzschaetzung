using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public class FactorTests
{
    static long? Factor(long count, long size, Unit? packBase, string unitCode, Unit? recipe) =>
        Matcher.Factor(count == 0 ? null : new Pack(count, size, packBase), unitCode, recipe);

    [Theory]
    [InlineData(20, 500, Unit.Ml, "XCS", Unit.Ml, 10000L)]
    [InlineData(1, 50000, Unit.Ml, "XKG", Unit.Ml, 50000L)]
    [InlineData(1, 25000, Unit.G, "XSA", Unit.G, 25000L)]
    [InlineData(24, 330, null, "XCS", Unit.Ml, 7920L)]
    [InlineData(24, 330, null, "XCS", Unit.G, 7920L)]
    [InlineData(250, 1000, Unit.Piece, "XPK", Unit.Piece, 250L)]
    [InlineData(20, 500, Unit.Ml, "", Unit.Ml, 10000L)]
    public void AContainerIsOnlyKnownFromItsPackSize(long count, long size, Unit? packBase, string unitCode, Unit? recipe, long factor) =>
        Assert.Equal(factor, Factor(count, size, packBase, unitCode, recipe));

    [Theory]
    [InlineData(0, 0, null, "KGM", Unit.G)]
    [InlineData(1, 25000, Unit.G, "KGM", Unit.G)]
    [InlineData(1, 750, Unit.Ml, "LTR", Unit.Ml)]
    [InlineData(1, 750, Unit.Ml, "MLT", Unit.Ml)]
    [InlineData(10, 1000, Unit.Piece, "H87", Unit.Piece)]
    public void AUnitTheTableConvertsNeedsNone(long count, long size, Unit? packBase, string unitCode, Unit? recipe) =>
        Assert.Null(Factor(count, size, packBase, unitCode, recipe));

    [Theory]
    [InlineData(1, 750, Unit.Ml, "XBO", Unit.G)]
    [InlineData(1, 25000, Unit.G, "XSA", Unit.Ml)]
    [InlineData(6, 330, Unit.Ml, "XCS", Unit.Piece)]
    [InlineData(6, 330, null, "XCS", Unit.Piece)]
    [InlineData(250, 1000, Unit.Piece, "XPK", Unit.G)]
    public void ASizeInAnotherBaseUnitIsRejected(long count, long size, Unit? packBase, string unitCode, Unit? recipe) =>
        Assert.Null(Factor(count, size, packBase, unitCode, recipe));

    [Theory]
    [InlineData("XCS", Unit.Ml)]
    [InlineData("KGM", Unit.Ml)]
    [InlineData("", Unit.G)]
    public void WithoutAPackSizeOnlyAHumanKnowsTheFactor(string unitCode, Unit recipe) =>
        Assert.Null(Matcher.Factor(null, unitCode, recipe));

    [Fact]
    public void AnIngredientInNoRecipeHasNoUnitToConvertTo() =>
        Assert.Null(Matcher.Factor(new Pack(20, 500, Unit.Ml), "XCS", null));
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

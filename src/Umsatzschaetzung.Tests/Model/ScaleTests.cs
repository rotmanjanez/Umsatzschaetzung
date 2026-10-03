using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class ScaleTests
{
    static RuleSet Rules(string unit)
    {
        var rs = new RuleSet();
        rs.Put(new Product { Id = "prod.bier", Name = "Pils", Unit = unit });
        return rs;
    }

    [Theory]
    [InlineData("MLT", Unit.Ml)]
    [InlineData("LTR", Unit.Ml)]
    [InlineData("KGM", Unit.G)]
    [InlineData("H87", Unit.Piece)]
    public void TheProductsOwnUnitDecidesItsScale(string unit, Unit scale) => Assert.Equal(scale, Scale.Of(Rules(unit), "prod.bier"));

    [Theory]
    [InlineData("Schaufel")]
    [InlineData("XBO")]
    [InlineData("")]
    public void AProductCountedInNoKnownUnitHasNoScale(string unit) => Assert.Null(Scale.Of(Rules(unit), "prod.bier"));

    [Fact]
    public void AnUnknownProductHasNoScale() => Assert.Null(Scale.Of(Rules("MLT"), "prod.fehlt"));

    [Theory]
    [InlineData(3, "KGM", 3000)]
    [InlineData(3, "kg", 3000)]
    [InlineData(2, "CLT", 20)]
    [InlineData(-2, "LTR", -2000)]
    [InlineData(3, "XBO", 3)]
    [InlineData(3, "Schaufel", 3)]
    [InlineData(0, "KGM", 0)]
    public void ToBaseMultipliesByTheUnitFactor(long amount, string unit, long expected) =>
        Assert.Equal(expected, Scale.ToBase(amount, unit));
}

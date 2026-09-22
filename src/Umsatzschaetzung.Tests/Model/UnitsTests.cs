using System.Text.Json;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class UnitsTests
{
    [Fact]
    public void NoTwoAliasesFoldTogether() => Assert.True(Units.NoFoldCollisions);

    [Theory]
    [InlineData("KGM", "KGM")]
    [InlineData("kgm", "KGM")]
    [InlineData(" LTR ", "LTR")]
    [InlineData("kg", "KGM")]
    [InlineData("KG", "KGM")]
    [InlineData("Stk.", "H87")]
    [InlineData("Stück", "H87")]
    [InlineData("Fl", "XBO")]
    [InlineData("Fl.", "XBO")]
    [InlineData("fässer", "XKG")]
    public void LookupFindsACodeOrAnAlias(string text, string code) => Assert.Equal(code, Units.Lookup(text)?.Code);

    [Theory]
    [InlineData("")]
    [InlineData("Zi")]
    [InlineData("k g")]
    [InlineData(".")]
    public void LookupMissesWhatTheTableDoesNotKnow(string text) => Assert.Null(Units.Lookup(text));

    [Theory]
    [InlineData("KGM", Unit.G, 1000, false)]
    [InlineData("GRM", Unit.G, 1, false)]
    [InlineData("LTR", Unit.Ml, 1000, false)]
    [InlineData("CLT", Unit.Ml, 10, false)]
    [InlineData("MLT", Unit.Ml, 1, false)]
    [InlineData("H87", Unit.Piece, 1, false)]
    [InlineData("XBO", Unit.Piece, 1, true)]
    [InlineData("XKG", Unit.Piece, 1, true)]
    public void AUnitKnowsItsBaseFactorAndWhetherItIsAContainer(string code, Unit unit, long factor, bool container)
    {
        var u = Units.Lookup(code)!;
        Assert.Equal((unit, factor, container), (u.Base, u.Factor, u.Container));
    }

    [Fact]
    public void EveryUnitInTheTableLooksUpToItselfAndEveryAliasToTheSameMeasure()
    {
        using var json = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(TestData.Repo, "data", "units.json")));
        foreach (var e in json.RootElement.GetProperty("units").EnumerateArray())
        {
            var u = Units.Lookup(e.GetProperty("code").GetString()!)!;
            Assert.Equal(e.GetProperty("code").GetString(), u.Code);
            Assert.Equal(e.GetProperty("name").GetString(), Units.Label(u.Code));
            Assert.Equal(u.Container, u.Code.StartsWith('X'));
            foreach (var a in e.GetProperty("aliases").EnumerateArray())
            {
                var alias = Units.Lookup(a.GetString()!)!;
                Assert.Equal((u.Base, u.Factor, u.Container), (alias.Base, alias.Factor, alias.Container));
            }
        }
    }

    [Theory]
    [InlineData("kg", "KGM")]
    [InlineData("Fl", "XBO")]
    [InlineData("STK", "H87")]
    public void ResolveReadsAnAliasExactly(string text, string code) => Assert.Equal(code, Units.Resolve(text));

    [Theory]
    [InlineData("KGM")]
    [InlineData("F1")]
    [InlineData(" kg")]
    public void ResolveDoesNotGuess(string text) => Assert.Null(Units.Resolve(text));

    [Theory]
    [InlineData("F1", "XBO")]
    [InlineData("EI", "XBO")]
    [InlineData("17F1", "XBO")]
    [InlineData("Bt)", "XBG")]
    [InlineData("K1ste", "XCS")]
    [InlineData("Kart0n", "XCT")]
    public void UnconfuseRepairsOcrLookalikes(string text, string code) => Assert.Equal(code, Units.Unconfuse(text));

    [Theory]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("17l")]
    [InlineData("")]
    [InlineData("Zi")]
    public void UnconfuseLeavesAStrayCharacterAlone(string text) => Assert.Null(Units.Unconfuse(text));

    [Theory]
    [InlineData(Unit.Ml, "ml")]
    [InlineData(Unit.G, "g")]
    [InlineData(Unit.Piece, "piece")]
    public void CodeNamesTheBaseUnit(Unit unit, string code) => Assert.Equal(code, Units.Code(unit));

    [Theory]
    [InlineData("KGM", "Kilogramm")]
    [InlineData("kg", "Kilogramm")]
    [InlineData("XBO", "Flasche")]
    [InlineData("ZZZ", "ZZZ")]
    [InlineData("", "")]
    public void LabelNamesTheUnitOrEchoesTheCode(string code, string label) => Assert.Equal(label, Units.Label(code));
}

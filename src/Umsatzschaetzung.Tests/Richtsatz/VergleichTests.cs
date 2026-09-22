using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

public class VergleichTests
{
    static readonly Sammlung Shipped2023 = Json.Deserialize<Sammlung>(Pdfs.ShippedJson(2023));

    static Sätze Auf(int von, int bis, int mittel) => new(new Satz(von, bis, mittel), null, null, null, null);

    static readonly Klasse Bäckerei = new("Bäckerei", null, ["10710.0", "47240.0"],
    [
        new("A", null, 25_000_000, Auf(100, 200, 150)),
        new("B", 25_000_000, 50_000_000, Auf(110, 210, 160)),
        new("C", 50_000_000, null, Auf(120, 220, 170)),
    ], null, 11);

    static readonly Klasse Imbiss = new("Imbissbetriebe", null, ["56102.0"], [new(null, null, null, Auf(150, 300, 200))], null, 13);

    static readonly Klasse Handwerk = new("Schreinerei", null, ["16230.0"],
        [new(null, null, null, new Sätze(null, new Satz(null, null, 60), new Satz(40, 70, 55), null, null))], null, 16);

    static readonly Klasse Kiosk = new("Kioske", null, ["56309.0", "47110.0"], [new(null, null, null, new Sätze(null, null, null, null, null))], "siehe", 15);

    static readonly Klasse Lebensmittel = new("Nahrungsmittel", null, ["47110.0"], [new(null, null, null, Auf(30, 60, 40))], null, 16);

    static readonly Klasse Pension = new("Pension", null, ["56101.1"], [new(null, null, null, Auf(200, 500, 300))], null, 12);

    static readonly Klasse Gaststätte = new("Gaststätte", null, ["56101.0"], [new(null, null, null, Auf(180, 400, 260))], null, 14);

    static Sammlung S(params Klasse[] klassen) => new(2024, [.. klassen], [], []);

    [Fact]
    public void TheGewerbekennzahlFindsItsRahmensatz()
    {
        var rahmen = Vergleich.Aufschlag(Shipped2023, "56101.0", 12_000_000);

        Assert.NotNull(rahmen);
        Assert.Equal(new Rahmen(2023, "Gast-, Speise- und Schankwirtschaften", null, new Satz(178, 400, 257)), rahmen);
        Assert.Equal((178, 400), (rahmen.Von, rahmen.Bis));
    }

    [Fact]
    public void AKennzahlThatFitsSeveralGewerbeklassenHasNoRahmensatz() =>
        Assert.Null(Vergleich.Aufschlag(Shipped2023, "561", 12_000_000));

    [Theory]
    [InlineData(0, "A")]
    [InlineData(10_000_000, "A")]
    [InlineData(25_000_000, "A")]
    [InlineData(25_000_001, "B")]
    [InlineData(50_000_000, "B")]
    [InlineData(50_000_001, "C")]
    [InlineData(long.MaxValue, "C")]
    public void TheStaffelIsTheOneTheRevenueFallsInBisInclusiveÜberExclusive(long umsatz, string stufe) =>
        Assert.Equal(stufe, Vergleich.Aufschlag(S(Bäckerei), "10710.0", umsatz)?.Stufe);

    [Fact]
    public void AStaffelKeepsItsOwnSatz()
    {
        Assert.Equal(new Satz(110, 210, 160), Vergleich.Aufschlag(S(Bäckerei), "10710.0", 30_000_000)?.Aufschlag);
        Assert.Equal(new Satz(120, 220, 170), Vergleich.Aufschlag(S(Bäckerei), "10710.0", 90_000_000)?.Aufschlag);
    }

    [Fact]
    public void AnUnstaffeltKlasseFitsAnyRevenue()
    {
        Assert.Null(Vergleich.Aufschlag(S(Imbiss), "56102.0", 0)?.Stufe);
        Assert.Equal(new Satz(150, 300, 200), Vergleich.Aufschlag(S(Imbiss), "56102.0", 0)?.Aufschlag);
        Assert.Equal(new Satz(150, 300, 200), Vergleich.Aufschlag(S(Imbiss), "56102.0", 10_000_000_000)?.Aufschlag);
    }

    [Fact]
    public void AGapBetweenStaffelnHasNoRahmensatz()
    {
        var lücke = Bäckerei with { Staffeln = [Bäckerei.Staffeln[0], Bäckerei.Staffeln[2]] };

        Assert.Null(Vergleich.Aufschlag(S(lücke), "10710.0", 30_000_000));
    }

    [Theory]
    [InlineData("56102.0")]
    [InlineData("56102")]
    [InlineData("5610")]
    [InlineData("5")]
    public void TheKennzahlMayBeGivenFullOrAsADigitPrefix(string kennzahl) =>
        Assert.Equal("Imbissbetriebe", Vergleich.Aufschlag(S(Imbiss, Bäckerei), kennzahl, 0)?.Klasse);

    [Theory]
    [InlineData("10710.0")]
    [InlineData("47240.0")]
    [InlineData("4724")]
    public void AnyKennzahlOfAKlasseFindsIt(string kennzahl) =>
        Assert.Equal("Bäckerei", Vergleich.Aufschlag(S(Bäckerei, Imbiss), kennzahl, 0)?.Klasse);

    [Theory]
    [InlineData("561")]
    [InlineData("56101")]
    public void APrefixOfKennzahlenInTwoKlassenIsAmbiguous(string kennzahl) =>
        Assert.Null(Vergleich.Aufschlag(S(Gaststätte, Pension), kennzahl, 0));

    [Fact]
    public void TheFullKennzahlTellsKlassenWithACommonPrefixApart()
    {
        Assert.Equal("Gaststätte", Vergleich.Aufschlag(S(Gaststätte, Pension), "56101.0", 0)?.Klasse);
        Assert.Equal("Pension", Vergleich.Aufschlag(S(Gaststätte, Pension), "56101.1", 0)?.Klasse);
    }

    [Fact]
    public void AKennzahlListedUnderTwoKlassenIsAmbiguous() =>
        Assert.Null(Vergleich.Aufschlag(S(Kiosk, Lebensmittel), "47110.0", 0));

    [Fact]
    public void TheShippedDoubleKennzahlenAreAmbiguous() =>
        Assert.Null(Vergleich.Aufschlag(Shipped2023, "47110.0", 12_000_000));

    [Theory]
    [InlineData("16230.0")]
    [InlineData("56309.0")]
    public void AKlasseWithoutAufschlagHasNoRahmensatz(string kennzahl) =>
        Assert.Null(Vergleich.Aufschlag(S(Handwerk, Kiosk), kennzahl, 12_000_000));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("99999.0")]
    [InlineData("56101.00")]
    public void AnUnknownOrMissingKennzahlHasNoRahmensatz(string? kennzahl) =>
        Assert.Null(Vergleich.Aufschlag(S(Gaststätte, Imbiss), kennzahl, 0));

    [Fact]
    public void WithoutASammlungThereIsNoRahmensatz() =>
        Assert.Null(Vergleich.Aufschlag(null, "56101.0", 0));

    [Fact]
    public void TheRahmenCarriesTheYearOfItsSammlung() =>
        Assert.Equal(2024, Vergleich.Aufschlag(S(Imbiss), "56102.0", 0)?.Jahr);
}

public class RahmenTests
{
    static readonly Rahmen Gast = new(2023, "Gast-, Speise- und Schankwirtschaften", null, new Satz(178, 400, 257));

    [Theory]
    [InlineData(25_700, Rahmenlage.Im)]
    [InlineData(45_000, Rahmenlage.Über)]
    [InlineData(10_000, Rahmenlage.Unter)]
    [InlineData(17_800, Rahmenlage.Im)]
    [InlineData(17_799, Rahmenlage.Unter)]
    [InlineData(40_000, Rahmenlage.Im)]
    [InlineData(40_001, Rahmenlage.Über)]
    [InlineData(0, Rahmenlage.Unter)]
    [InlineData(-5_000, Rahmenlage.Unter)]
    public void TheMarkupInBasisPointsIsReadAgainstTheRahmenInclusive(long markup, Rahmenlage lage) =>
        Assert.Equal(lage, Gast.Lage(markup));

    [Fact]
    public void TheMittelsatzLiesWithinTheRahmen()
    {
        Assert.Equal(257, Gast.Aufschlag.Mittel);
        Assert.Equal(Rahmenlage.Im, Gast.Lage(Gast.Aufschlag.Mittel * 100L));
    }

    [Fact]
    public void ASatzWithoutRahmenIsBoundedByItsMittelsatz()
    {
        var rahmen = new Rahmen(2023, "Friseur", "A", new Satz(null, null, 91));

        Assert.Equal((91, 91), (rahmen.Von, rahmen.Bis));
        Assert.Equal(Rahmenlage.Im, rahmen.Lage(9_100));
        Assert.Equal(Rahmenlage.Unter, rahmen.Lage(9_099));
        Assert.Equal(Rahmenlage.Über, rahmen.Lage(9_101));
    }

    [Fact]
    public void AHalfOpenSatzFallsBackToTheMittelsatzOnlyOnItsOpenSide()
    {
        var rahmen = new Rahmen(2023, "x", null, new Satz(100, null, 150));

        Assert.Equal((100, 150), (rahmen.Von, rahmen.Bis));
    }
}

using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

public class RichtsätzeTests
{
    static Klasse Klasse(Sammlung s, string name) => s.Klassen.Single(k => k.Name == name);

    [Theory]
    [InlineData(2012)]
    [InlineData(2020)]
    [InlineData(2025)]
    public void TheYearComesFromTheKalenderjahrInThePdf(int year) =>
        Assert.Equal(year, Pdfs.Sammlung(year).Year);

    [Theory]
    [InlineData(2012)]
    [InlineData(2025)]
    public void EveryGewerbeklasseOfTheShippedYearIsFound(int year)
    {
        var shipped = Json.Deserialize<Sammlung>(Pdfs.ShippedJson(year));

        Assert.Equal(shipped.Klassen.Select(k => k.Name), Pdfs.Sammlung(year).Klassen.Select(k => k.Name));
    }

    [Fact]
    public void TheRecentLayoutReadsAnUnstaffeltKlasseWithAllFourSätze()
    {
        var gast = Klasse(Pdfs.Sammlung(2025), "Gast-, Speise- und Schankwirtschaften");

        Assert.Equal(["56101.0", "56301.0"], gast.Kennzahlen);
        Assert.Equal(14, gast.Seite);
        var staffel = Assert.Single(gast.Staffeln);
        Assert.Equal(new Staffel(null, null, null, new Sätze(
            new Satz(178, 400, 257), new Satz(64, 80, 72), null, new Satz(29, 61, 47), new Satz(8, 39, 22))), staffel);
    }

    [Fact]
    public void TheRecentLayoutReadsThreeTurnoverBandsWithABareMittelsatzForRohgewinnI()
    {
        var schreinerei = Klasse(Pdfs.Sammlung(2025), "Schreinerei, Tischlerei (auch Bautischlerei und Bauschlosserei)");

        Assert.Equal(["16230.0", "31099.0", "43320.0"], schreinerei.Kennzahlen);
        Assert.Equal(
            [("A", null, 15_000_000), ("B", 15_000_000, 30_000_000), ("C", 30_000_000, null)],
            schreinerei.Staffeln.Select(s => (s.Stufe, s.Von, s.Bis)));
        Assert.Equal(new Sätze(null, new Satz(null, null, 64), new Satz(36, 72, 53), new Satz(17, 50, 34), new Satz(11, 43, 28)),
            schreinerei.Staffeln[1].Sätze);
    }

    [Fact]
    public void ABisPrintedSlightlyBelowItsAmountStillClosesTheBand()
    {
        var schreinerei = Klasse(Pdfs.Sammlung(2024), "Schreinerei, Tischlerei (auch Bautischlerei und Bauschlosserei)");

        Assert.Equal(30_000_000, schreinerei.Staffeln[1].Bis);
    }

    [Fact]
    public void AKlasseWithoutSätzeKeepsTheRemarkThatPointsElsewhere()
    {
        var kiosk = Klasse(Pdfs.Sammlung(2025), "Kioske und Verkaufsstände");

        Assert.Equal(["56309.0", "47260.0", "47621.0", "47110.0"], kiosk.Kennzahlen);
        Assert.Equal(new Sätze(null, null, null, null, null), Assert.Single(kiosk.Staffeln).Sätze);
        Assert.Equal("Je nach überwiegendem Warensortiment: - Nahrungs- und Genussmittel, Eh. - Tabakwaren und Zeitschriften, Eh.", kiosk.Bemerkung);
    }

    [Fact]
    public void TheOldLayoutReadsItsOwnRatesAndBands()
    {
        var s = Pdfs.Sammlung(2012);

        var gast = Klasse(s, "Gast-, Speise- und Schankwirtschaften");
        Assert.Equal(["56101.0", "56107.0", "56301.0"], gast.Kennzahlen);
        Assert.Equal(new Satz(186, 400, 257), Assert.Single(gast.Staffeln).Sätze.Aufschlag);
        Assert.StartsWith("bei Restaurants mit", gast.Bemerkung);

        var hotels = Klasse(s, "Hotels, Gasthöfe und Pensionen mit Halb und Vollpension");
        Assert.Equal(["55101.0", "55103.0", "55104.0"], hotels.Kennzahlen);
        Assert.Equal([("A", null, 50_000_000), ("B", 50_000_000, null)], hotels.Staffeln.Select(st => (st.Stufe, st.Von, st.Bis)));
        Assert.Equal(new Satz(257, 1329, 456), hotels.Staffeln[1].Sätze.Aufschlag);
        Assert.Equal(new Satz(4, 20, 12), hotels.Staffeln[1].Sätze.Reingewinn);

        var apotheken = Klasse(s, "Apotheken");
        Assert.Equal(11, apotheken.Seite);
        Assert.Equal(new Satz(30, 41, 35), Assert.Single(apotheken.Staffeln).Sätze.Aufschlag);
    }

    [Fact]
    public void SynonymePointAtTheirGewerbeklasse()
    {
        var recent = Pdfs.Sammlung(2025).Synonyme;
        Assert.Equal(179, recent.Count);
        Assert.Contains(new Synonym("Anstreicher", "Maler- und Lackierergewerbe"), recent);
        Assert.Contains(new Synonym("Bauschlosser", "Schreinerei, Tischlerei"), recent);
        Assert.Contains(new Synonym("Bekleidung, Eh.", "Textilwaren verschiedener Art und Oberbekleidung, Eh."), recent);
        Assert.Equal(new Synonym("Zigarren und Zigaretten, Eh.", "Tabakwaren und Zeitschriften, Eh."), recent[^1]);

        var old = Pdfs.Sammlung(2012).Synonyme;
        Assert.Equal(35, old.Count);
        Assert.Equal(new Synonym("Anstrichbedarf, Eh.", "Bau- und Heimwerkerbedarf, Anstrichmittel, Eh."), old[1]);
    }

    [Fact]
    public void AnEntryThatOnlyNamesAGewerbeklasseIsNoSynonym() =>
        Assert.DoesNotContain(Pdfs.Sammlung(2025).Synonyme, s => s.Begriff is "Bauunternehmen" or "Bestattungsunternehmen");

    [Fact]
    public void PauschbeträgeCoverTheCalendarYearAndTheNext()
    {
        var p = Pdfs.Sammlung(2025).Pauschbeträge;

        Assert.Equal(18, p.Count);
        Assert.Equal(new Pauschbetrag(new(2025, 1, 1), new(2025, 12, 31), "Bäckerei", 163_300, 20_900, 184_200), p[0]);
        Assert.Equal(new Pauschbetrag(new(2026, 1, 1), new(2026, 12, 31), "Obst, Gemüse, Südfrüchte und Kartoffeln (Eh.)", 38_400, 16_900, 55_300), p[^1]);
        Assert.All(p, b => Assert.Equal(b.Gesamt, b.Ermäßigt + b.Voll));
    }

    [Fact]
    public void ASplitGewerbezweigCarriesItsHeadingIntoEachPart()
    {
        var zweige = Pdfs.Sammlung(2025).Pauschbeträge.Select(p => p.Gewerbezweig).ToList();

        Assert.Contains("Gaststätten aller Art a) mit Abgabe von kalten Speisen", zweige);
        Assert.Contains("Gaststätten aller Art b) mit Abgabe von kalten und warmen Speisen", zweige);
        Assert.Contains("Gast- und Speisewirtschaften b) mit Abgabe von kalten und warmen Speisen",
            Pdfs.Sammlung(2012).Pauschbeträge.Select(p => p.Gewerbezweig));
    }

    [Fact]
    public void ATaxRateChangeSplitsThePauschbeträgeIntoHalfYears()
    {
        var p = Pdfs.Sammlung(2020).Pauschbeträge;

        Assert.Equal(36, p.Count);
        Assert.Equal(
            [
                (new DateOnly(2020, 1, 1), new DateOnly(2020, 6, 30)),
                (new DateOnly(2020, 7, 1), new DateOnly(2020, 12, 31)),
                (new DateOnly(2021, 1, 1), new DateOnly(2021, 6, 30)),
                (new DateOnly(2021, 7, 1), new DateOnly(2021, 12, 31)),
            ],
            p.Select(b => (b.Von, b.Bis)).Distinct());
        Assert.Equal(new Pauschbetrag(new(2020, 1, 1), new(2020, 6, 30), "Bäckerei", 60_900, 20_300, 81_200), p[0]);
    }

    [Fact]
    public void BytesThatAreNoPdfAreRefused() =>
        Assert.Throws<InvalidDataException>(() => Sheets.Read([1, 2, 3]));

    [Fact]
    public void APdfWithoutARichtsatzTableIsRefused()
    {
        var e = Assert.Throws<InvalidDataException>(() => Richtsätze.Read(Sheets.Read(File.ReadAllBytes(TestData.File("zugferd.pdf")))));
        Assert.Contains("keine Gewerbeklassen", e.Message);
    }
}

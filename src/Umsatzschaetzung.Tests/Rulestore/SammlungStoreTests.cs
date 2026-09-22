using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Rulestore;

namespace Umsatzschaetzung.Tests.Rulestore;

public class SammlungStoreTests
{
    static RuleStore Open(TempDir tmp) => new(tmp.Path, new RuleSet());

    static int[] Shipped() =>
    [
        .. typeof(RuleStore).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("richtsatz/", StringComparison.Ordinal))
            .Select(n => int.Parse(Path.GetFileNameWithoutExtension(n.AsSpan("richtsatz/".Length))))
            .OrderDescending(),
    ];

    static Sammlung ShippedSammlung(int year)
    {
        using var s = typeof(RuleStore).Assembly.GetManifestResourceStream($"richtsatz/{year}.json")!;
        using var m = new MemoryStream();
        s.CopyTo(m);
        return Json.Deserialize<Sammlung>(m.ToArray());
    }

    static Sammlung Handmade(int year) => new(year,
        [
            new Klasse("Gast-, Speise- und Schankwirtschaften", "mit Ausschank", ["56101.0", "56102"],
            [
                new Staffel("bis 250.000 €", null, 25_000_000, new Sätze(new Satz(178, 400, 257), null, new Satz(null, null, 70), null, new Satz(1, 2, 3))),
                new Staffel(null, 25_000_001, null, new Sätze(null, null, null, null, null)),
            ], "Bemerkung „ä“", 12),
            new Klasse("Ohne alles", null, [], [], null, 0),
        ],
        [new Synonym("Kneipe", "Gast-, Speise- und Schankwirtschaften"), new Synonym("Bar", "x")],
        [new Pauschbetrag(new DateOnly(year, 1, 1), new DateOnly(year, 6, 30), "Gaststätten", 1_234, 5_678, 6_912)]);

    [Fact]
    public void TheShippedSammlungenSeedThemselvesNewestFirst()
    {
        using var tmp = new TempDir();

        var infos = Open(tmp).Sammlungen();

        Assert.Equal(Shipped(), infos.Select(s => s.Year));
        Assert.True(infos.Count > 1);
        Assert.All(infos, s => Assert.True(s.Mitgeliefert));
        Assert.All(infos, s => Assert.True(s.Klassen > 0));
    }

    [Fact]
    public void AStoredSammlungReadsBackAsShipped()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);

        foreach (var year in Shipped())
            Assert.Equal(Json.Serialize(ShippedSammlung(year)), Json.Serialize(store.Sammlung(year)!));
        Assert.Equal(ShippedSammlung(Shipped()[0]).Klassen.Count, store.Sammlungen()[0].Klassen);
    }

    [Fact]
    public void AYearWithoutSammlungHasNone()
    {
        using var tmp = new TempDir();

        Assert.Null(Open(tmp).Sammlung(1999));
    }

    [Fact]
    public void AnImportedSammlungRoundTripsWithEveryOpenEnd()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);

        var infos = store.ImportSammlung(Handmade(1999), "rs-1999.pdf");

        Assert.Equal(Json.Serialize(Handmade(1999)), Json.Serialize(store.Sammlung(1999)!));
        var info = Assert.Single(infos, s => s.Year == 1999);
        Assert.Equal(("rs-1999.pdf", 2, false), (info.Quelle, info.Klassen, info.Mitgeliefert));
        Assert.Equal(1999, infos[^1].Year);
    }

    [Fact]
    public void AnImportTakesThePlaceOfItsYearAndSurvivesTheNextStart()
    {
        using var tmp = new TempDir();
        var year = Shipped()[0];
        Open(tmp).ImportSammlung(Handmade(year), "eigene.pdf");

        var store = Open(tmp);

        Assert.Equal(Json.Serialize(Handmade(year)), Json.Serialize(store.Sammlung(year)!));
        Assert.Equal("eigene.pdf", store.Sammlungen()[0].Quelle);
        Assert.Equal(Shipped().Length, store.Sammlungen().Count);
    }

    [Fact]
    public void ADroppedShippedSammlungIsSeededAgainOnTheNextStart()
    {
        using var tmp = new TempDir();
        var year = Shipped()[0];
        var store = Open(tmp);

        var after = store.DeleteSammlung(year);

        Assert.Equal(Shipped().Length - 1, after.Count);
        Assert.Null(store.Sammlung(year));
        var again = Open(tmp).Sammlungen();
        Assert.Contains(again, s => s.Year == year && s.Mitgeliefert);
    }

    [Fact]
    public void ADroppedImportedSammlungStaysGone()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        store.ImportSammlung(Handmade(1999), "rs.pdf");
        store.DeleteSammlung(1999);

        Assert.DoesNotContain(Open(tmp).Sammlungen(), s => s.Year == 1999);
    }

    [Fact]
    public void SammlungenDoNotTouchTheRuleVersion()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        store.ImportSammlung(Handmade(1999), "rs.pdf");
        store.DeleteSammlung(Shipped()[0]);

        Assert.Equal(0, store.Load().Version);
    }
}

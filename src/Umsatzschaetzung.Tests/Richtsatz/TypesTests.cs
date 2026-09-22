using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

public class TypesTests
{
    static readonly Sammlung Hand = new(2025,
        [
            new Klasse("Bäckerei", "Brot- und Feinbäckerei", ["10710.0"],
            [
                new("A", null, 25_000_000, new Sätze(new Satz(156, 400, 245), new Satz(null, null, 71), null, null, null)),
                new("B", 25_000_000, null, new Sätze(null, null, null, null, new Satz(4, 17, 10))),
            ], "Bemerkung", 11),
            new Klasse("Kioske", null, [], [new(null, null, null, new Sätze(null, null, null, null, null))], null, 15),
        ],
        [new Synonym("Anstreicher", "Maler- und Lackierergewerbe")],
        [new Pauschbetrag(new(2025, 1, 1), new(2025, 6, 30), "Bäckerei", 163_300, 20_900, 184_200)]);

    [Fact]
    public void ASammlungSurvivesAJsonRoundTrip()
    {
        var back = Json.Deserialize<Sammlung>(Json.Serialize(Hand));

        Assert.Equal(Json.Serialize(Hand), Json.Serialize(back));
        Assert.Equal(Hand.Klassen[0].Staffeln, back.Klassen[0].Staffeln);
        Assert.Equal(Hand.Synonyme, back.Synonyme);
        Assert.Equal(Hand.Pauschbeträge, back.Pauschbeträge);
    }

    [Fact]
    public void JsonLeavesOutWhatIsNullAndKeepsTheUmlauts()
    {
        var json = Json.Serialize(Hand);

        Assert.Contains("\"pauschbeträge\"", json);
        Assert.Contains("\"sätze\": {}", json);
        Assert.Contains("\"von\": \"2025-01-01\"", json);
        Assert.DoesNotContain("null", json);
        Assert.DoesNotContain("\\u", json);
    }

    [Fact]
    public void RecordsWithEqualValuesAreEqual()
    {
        Assert.Equal(new Satz(1, 2, 3), new Satz(1, 2, 3));
        Assert.Equal(new Staffel("A", null, 5, new Sätze(new Satz(1, 2, 3), null, null, null, null)),
            new Staffel("A", null, 5, new Sätze(new Satz(1, 2, 3), null, null, null, null)));
        Assert.NotEqual(new Satz(null, null, 3), new Satz(3, 3, 3));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("richtsatzsammlung-2025.pdf", false)]
    public void ASammlungWithoutQuelleIsTheShippedOne(string quelle, bool mitgeliefert) =>
        Assert.Equal(mitgeliefert, new SammlungInfo(2025, 73, quelle, DateTimeOffset.UnixEpoch).Mitgeliefert);
}

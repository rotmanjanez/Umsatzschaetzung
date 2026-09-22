using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

namespace Umsatzschaetzung.Tests.Richtsatz;

public class ShippedSammlungTests
{
    public static TheoryData<int> Years() => [.. Pdfs.ShippedYears()];

    [Theory]
    [MemberData(nameof(Years))]
    public void ParsingTheSourcePdfReproducesTheShippedJson(int year)
    {
        var expected = Pdfs.ShippedJson(year);
        Assert.Equal(expected, Json.Serialize(Pdfs.Sammlung(year)));
    }

    [Theory]
    [MemberData(nameof(Years))]
    public void EveryShippedYearIsEmbeddedInCoreAsItIsOnDisk(int year)
    {
        using var stream = typeof(Sammlung).Assembly.GetManifestResourceStream($"richtsatz/{year}.json");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);

        Assert.Equal(Pdfs.ShippedJson(year), reader.ReadToEnd());
    }

    [Fact]
    public void EveryFixturePdfHasAShippedJson()
    {
        var pdfs = Directory.GetFiles(TestData.Fixture("richtsatzsammlung"), "*.pdf")
            .Select(f => int.Parse(Path.GetFileNameWithoutExtension(f)["richtsatzsammlung-".Length..])).Order();

        Assert.Equal(pdfs, Pdfs.ShippedYears());
    }

    [Theory]
    [MemberData(nameof(Years))]
    public void EveryParsedYearHoldsItsYearAndStaffelnThatChainWithoutGaps(int year)
    {
        var s = Pdfs.Sammlung(year);

        Assert.Equal(year, s.Year);
        Assert.InRange(s.Klassen.Count, 70, 80);
        foreach (var k in s.Klassen)
        {
            Assert.NotEmpty(k.Staffeln);
            Assert.Null(k.Staffeln[0].Von);
            Assert.Null(k.Staffeln[^1].Bis);
            for (var i = 1; i < k.Staffeln.Count; i++)
                Assert.True(k.Staffeln[i - 1].Bis == k.Staffeln[i].Von, $"{year} {k.Name}: Stufe {k.Staffeln[i].Stufe} does not start where {k.Staffeln[i - 1].Stufe} ends");
        }
    }
}

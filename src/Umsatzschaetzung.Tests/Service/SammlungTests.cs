using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class SammlungTests : IDisposable
{
    readonly Host host = new();
    readonly IService svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public SammlungTests() => svc = host.Service;

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task TheShippedSammlungenAreListedNewestFirst()
    {
        var sammlungen = await svc.Sammlungen(ct);
        Assert.True(sammlungen.Count > 1);
        Assert.All(sammlungen, s => Assert.True(s.Mitgeliefert && s.Klassen > 0));
        Assert.Equal(sammlungen.Select(s => s.Year).OrderDescending(), sammlungen.Select(s => s.Year));
    }

    [Fact]
    public async Task ADroppedSammlungIsGoneUntilTheNextStart()
    {
        var before = await svc.Sammlungen(ct);
        var year = before[0].Year;

        var after = await svc.DeleteSammlung(year, ct);

        Assert.Equal(before.Count - 1, after.Count);
        Assert.DoesNotContain(after, s => s.Year == year);
        Assert.Contains(new RuleStore(host.Store.Dir, TestData.Seed()).Sammlungen(), s => s.Year == year && s.Mitgeliefert);
    }

    [Fact]
    public async Task DroppingAYearWithoutSammlungIsNotFound()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.DeleteSammlung(1999, ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[0])]
    public async Task ABrokenPdfIsInvalid(byte[] pdf)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.ImportSammlung("kaputt.pdf", pdf, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
    }

    [Fact]
    public async Task AnImportedPdfTakesThePlaceOfItsYearAndCanBeDroppedAgain()
    {
        var year = (await svc.Sammlungen(ct))[0].Year;
        var name = $"richtsatzsammlung-{year}.pdf";

        var imported = await svc.ImportSammlung(name, File.ReadAllBytes(TestData.Fixture("richtsatzsammlung/" + name)), ct);

        var replaced = Assert.Single(imported, s => s.Year == year);
        Assert.False(replaced.Mitgeliefert);
        Assert.Equal(name, replaced.Quelle);
        Assert.True(replaced.Klassen > 0);
        Assert.DoesNotContain(await svc.DeleteSammlung(year, ct), s => s.Year == year);
    }
}

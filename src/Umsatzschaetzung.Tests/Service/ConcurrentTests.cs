using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Service;

public sealed class ConcurrentTests : IDisposable
{
    sealed class Held : IRanking
    {
        public readonly TaskCompletionSource Asked = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Go = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<Ranked>> Rank(RuleSet rs, string gewerbe, InvoiceLine line, DateOnly date, int count, CancellationToken ct = default)
        {
            Asked.TrySetResult();
            await Go.Task.WaitAsync(ct);
            return [];
        }

        public Task Warm(RuleSet rs, string gewerbe, bool open, CancellationToken ct = default) => Task.CompletedTask;
    }

    readonly Held ranking = new();
    readonly Host host;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public ConcurrentTests()
    {
        host = new Host(ranking: ranking);
        host.Store.Save(new ArticleMapping
        {
            Id = "map.zwickl", SupplierName = MatcherHost.Rheinland, SupplierArticleId = "Z-1", Observed = "Zwickl naturtrueb, Keg 30 l",
            IngredientId = "ing.bier.fass", Factor = 30000, Confirmed = true,
        });
    }

    public void Dispose() => host.Dispose();

    static Invoice Delivery(string id, params InvoiceLine[] lines) => new()
    {
        Id = id, Source = Source.Ubl, SupplierName = MatcherHost.Rheinland, Number = id, Date = new DateOnly(2024, 4, 1), Lines = [.. lines],
    };

    // Mapping a case asks the matcher line by line for seconds; an import goes on storing meanwhile.
    [Fact]
    public async Task AnInvoiceStoredWhileTheCaseIsMappedStaysInIt()
    {
        var kase = Vorlage.Blank("Parallel", "56101.0");
        kase.Invoices =
        [
            Delivery("re-a",
                new InvoiceLine { No = 1, Name = "Zwickl naturtrueb, Keg 50 l", SellerArticleId = "Z-1", UnitCode = "XKG", Quantity = 1000 },
                new InvoiceLine { No = 2, Name = "Servietten 3-lagig 250 Stk", UnitCode = "H87", Quantity = 1000 }),
        ];
        var c = await host.Service.Cases.Put(kase, ct);

        var mapping = host.Service.Mapping.Map(c.Id, ct);
        await ranking.Asked.Task.WaitAsync(ct);
        await host.Service.Invoices.Verify(new VerifyReq(c.Id,
            Delivery("re-b", new InvoiceLine { No = 1, Name = "Kerzen weiß", UnitCode = "H87", Quantity = 1000 }), Intent.Store, null, null), ct);
        ranking.Go.SetResult();
        var mapped = await mapping;

        var saved = await host.Service.Cases.Get(c.Id, ct);
        Assert.Equal(["re-a", "re-b"], saved.Invoices.Select(i => i.Id));
        Assert.Equal("map.zwickl", saved.Invoices[0].Lines[0].MappingId);
        Assert.Null(saved.MappedStore);
        Assert.Null(mapped.MappedStore);
    }
}

using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

// The services of another runtime answer as the local ones do: every call crosses as a message
// whose files arrive as copies, as they would through postMessage.
public sealed class WireTests : IDisposable
{
    readonly Host host = new();
    readonly Services svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public WireTests() => svc = Remote.Over(new Copying(new Dispatch(host.Service)));

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task ACaseComesBackAsTheLocalServicesKeepIt()
    {
        var put = await svc.Cases.Put(Vorlage.Load(), ct);
        Assert.Equal(Json.Serialize(await host.Service.Cases.Get(put.Id, ct)), Json.Serialize(await svc.Cases.Get(put.Id, ct)));
    }

    [Fact]
    public async Task FilesCrossWholeBothWays()
    {
        var kase = await host.PutVorlage();
        var pdf = File.ReadAllBytes(TestData.File("zugferd.pdf"));
        await svc.Invoices.Verify(new VerifyReq(kase.Id, new Invoice { Number = "Z" }, Intent.Store, "zugferd.pdf", pdf), ct);

        var dump = await svc.Cases.Export(kase.Id, ct);
        Assert.Equal((await host.Service.Cases.Export(kase.Id, ct)).Data.Length, dump.Data.Length);

        await svc.Cases.Delete(kase.Id, ct);
        Assert.DoesNotContain((await svc.Cases.List(ct)).Cases, c => c.Id == kase.Id);
        Assert.Equal(2, (await svc.Cases.Import(dump.FileName, dump.Data, false, ct)).Invoices.Count);
    }

    [Fact]
    public async Task ARefusalKeepsItsCodeMessageAndDetails()
    {
        var kase = await host.PutVorlage();
        var dump = await svc.Cases.Export(kase.Id, ct);
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Cases.Import(dump.FileName, dump.Data, false, ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.Equal([kase.Label], Assert.IsType<List<string>>(e.Details));
    }

    [Fact]
    public async Task EveryKindOfRuleIsSaved()
    {
        var rs = await svc.Rules.Load(ct);
        IRuleEntity[] rules =
        [
            rs.Categories.Values.First(), rs.Ingredients.Values.First(), rs.Mappings.Values.First(), rs.Products.Values.First(),
            rs.YieldRules.Values.First(), rs.Gewerbezweige.Values.First(), new ReportTemplate { Id = "tpl.draht", Name = "Draht", Source = "<p></p>" },
        ];
        foreach (var rule in rules) rs = await svc.Rules.Save(rule, ct);
        Assert.Contains("tpl.draht", rs.Templates.Keys);
        Assert.Equal(Json.Serialize(await host.Service.Rules.Load(ct)), Json.Serialize(rs));
    }

    [Fact]
    public async Task TheRulesCrossOnlyWhenTheyChanged()
    {
        var calls = new Counting(new Dispatch(host.Service));
        var rules = Remote.Over(calls).Rules;
        var first = await rules.Load(ct);
        Assert.Same(first, await rules.Load(ct));
        Assert.Equal([true, false], calls.Sets);

        await host.Service.Rules.Save(Json.Copy(first.Categories.Values.First()), ct);
        var changed = await rules.Load(ct);
        Assert.True(changed.Version > first.Version);
        Assert.Equal(Json.Serialize(await host.Service.Rules.Load(ct)), Json.Serialize(changed));

        var saved = await rules.Save(Json.Copy(changed.Categories.Values.First()), ct);
        Assert.Same(saved, await rules.Load(ct));
        Assert.Equal([true, false, true, false], calls.Sets);
    }

    [Fact]
    public async Task AnUnknownCallIsRefused()
    {
        var answer = await new Dispatch(host.Service).Handle("cases.shred", Message.Empty, ct);
        Assert.Equal(ErrorCode.NotFound, answer.Fault?.Code);
    }

    [Fact]
    public async Task ACancelledCallIsCancelledThere()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.Cases.List(cancelled.Token));
    }
}

sealed class Counting(Dispatch to) : ITransport
{
    public List<bool> Sets { get; } = [];

    public async Task<Message> Call(string method, Message request, CancellationToken ct)
    {
        var answer = await to.Handle(method, request, ct);
        if (method == "rules.load") Sets.Add(answer.Json != "null");
        return answer;
    }
}

sealed class Copying(Dispatch to) : ITransport
{
    public async Task<Message> Call(string method, Message request, CancellationToken ct)
    {
        var answer = await to.Handle(method, new(request.Json, [.. request.Blobs.Select(b => b.ToArray())]), ct);
        return answer with { Blobs = [.. answer.Blobs.Select(b => b.ToArray())] };
    }
}

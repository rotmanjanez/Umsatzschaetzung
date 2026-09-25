using System.Reflection;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

[Collection(MatcherCollection.Name)]
public class MappingTests(MatcherHost host)
{
    // The matcher cannot be stopped within a line, so a run the visit left keeps going until that
    // line is done; the next visit has to map what the left one gave up.
    [Fact]
    public async Task EnteringAgainWhileTheLeftRunWindsDownStillMapsTheOpenLines()
    {
        var kase = Vorlage.Load();
        kase.Id = "case.mapping.again";
        foreach (var line in kase.Invoices.SelectMany(i => i.Lines)) line.MappingId = null;
        kase.MappedAt = 0;
        await host.Service.PutCase(kase, CancellationToken.None);

        var service = Slow.Over(host.Service);
        var session = new Session(service);
        session.Open(await service.GetCase(kase.Id, CancellationToken.None));
        Assert.True(await session.LoadRules(CancellationToken.None));
        var model = new MappingModel();
        foreach (var g in LineGroup.Of(session.Case, session.Rules)) model.Groups.Add(g);

        using CancellationTokenSource first = new(), second = new();
        var left = model.MapOpen(session, first.Token);
        first.Cancel();
        var back = model.MapOpen(session, second.Token);
        ((Slow)(object)service).Line.SetResult();
        await left;
        await back;

        Assert.Equal(session.Rules!.Version, session.Case!.MappedAt);
    }

    // The case is mapped once the line the matcher is on is done.
    public class Slow : DispatchProxy
    {
        IService inner = null!;

        public TaskCompletionSource Line { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static IService Over(IService inner)
        {
            var proxy = Create<IService, Slow>();
            ((Slow)(object)proxy).inner = inner;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method!.Name == nameof(IService.MapCase)) return MapCase((string)args![0]!, (CancellationToken)args[1]!);
            try
            {
                return method.Invoke(inner, args);
            }
            catch (TargetInvocationException e)
            {
                throw e.InnerException!;
            }
        }

        async Task<Case> MapCase(string caseId, CancellationToken ct)
        {
            await Line.Task;
            return await inner.MapCase(caseId, ct);
        }
    }
}

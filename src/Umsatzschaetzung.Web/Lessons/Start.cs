using System.Runtime.InteropServices.JavaScript;
using Avalonia.Controls;
using Umsatzschaetzung.Browser;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Lessons;

// The web app with a coach over it: its services run in their worker over the stores a
// headless run kept where the lesson starts (see the `keep` step), and the page opens its case.
public static partial class Coach
{
    static readonly WorkerTransport transport = new();

    public static async Task Start(string element, string? open)
    {
        await WorkerTransport.Start();
        var service = Remote.Over(transport);
        var view = await Host.Start(service, element);
        if (!view.IsLoaded)
        {
            var loaded = new TaskCompletionSource();
            view.Loaded += (_, _) => loaded.TrySetResult();
            await loaded.Task;
        }
        Attach(TopLevel.GetTopLevel(view)!, view.Session);
        if (open is not null) view.Session.Open(await service.Cases.Get(open, CancellationToken.None));
        Ready();
    }

    [JSImport("ready", "coach")]
    static partial void Ready();
}

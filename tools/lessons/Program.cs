using System.Globalization;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Umsatzschaetzung.Browser;
using Umsatzschaetzung.Lessons;
using Umsatzschaetzung.Service;

[assembly: SupportedOSPlatform("browser")]

// The web app with a coach over it: its services run in their worker over the stores a
// headless run kept where the lesson starts (see the `keep` step), and the page opens its case.
if (args[0] == "service")
{
    await Serve.Start();
    return;
}

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
await WorkerTransport.Start();
var service = Remote.Over(new WorkerTransport());
var view = await Host.Start(service, args[0]);
if (!view.IsLoaded)
{
    var loaded = new TaskCompletionSource();
    view.Loaded += (_, _) => loaded.TrySetResult();
    await loaded.Task;
}
Coach.Attach(TopLevel.GetTopLevel(view)!, view.Session);
if (args.Length > 1) view.Session.Open(await service.Cases.Get(args[1], CancellationToken.None));
Ready();

static partial class Program
{
    [System.Runtime.InteropServices.JavaScript.JSImport("ready", "coach")]
    static partial void Ready();
}

using System.Globalization;
using System.Runtime.Versioning;
using Umsatzschaetzung.Browser;
using Umsatzschaetzung.Lessons;
using Umsatzschaetzung.Service;

[assembly: SupportedOSPlatform("browser")]

// The page runs the interface alone; the services run in a worker of their own, started with
// "service", and the weights in one of theirs, fetched the first time a model is asked for.
// A lesson's page starts it with "lesson", with the coach over it. The program reads German.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
if (args[0] == "service")
    await Serve.Start();
else if (args[0] == "lesson")
    await Coach.Start(args[1], args.ElementAtOrDefault(2));
else
{
    await WorkerTransport.Start();
    Umsatzschaetzung.App.App.SingleViewForget = Host.Forget;
    await Host.Start(Remote.Over(new WorkerTransport()), args[0]);
}

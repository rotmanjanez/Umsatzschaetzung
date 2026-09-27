using System.Runtime.Versioning;
using Umsatzschaetzung.Browser;
using Umsatzschaetzung.Service;

[assembly: SupportedOSPlatform("browser")]

// The page runs the interface alone; the services run in a worker of their own, started with
// "service", and the weights in one of theirs, fetched the first time a model is asked for.
if (args[0] == "service")
    await Serve.Start();
else
{
    await WorkerTransport.Start();
    await Host.Start(Remote.Over(new WorkerTransport()), args[0]);
}

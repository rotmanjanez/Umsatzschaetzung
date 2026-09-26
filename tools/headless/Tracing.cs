using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;

namespace Umsatzschaetzung.Headless;

// Samples every thread's stack while one step runs, into a .nettrace for dotnet-trace convert.
public sealed class Tracing(string dir)
{
    static readonly EventPipeProvider[] Providers =
    [
        new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational),
        new("Microsoft-Windows-DotNETRuntime", EventLevel.Informational, 0x4c14fccbd),
    ];

    public IDisposable Start(string name)
    {
        Directory.CreateDirectory(dir);
        var session = new DiagnosticsClient(Environment.ProcessId).StartEventPipeSession(Providers, true);
        var file = File.Create(Path.Combine(dir, name + ".nettrace"));
        var copy = session.EventStream.CopyToAsync(file);
        return new Stop(() =>
        {
            session.Stop();
            copy.Wait();
            file.Dispose();
            session.Dispose();
        });
    }

    sealed class Stop(Action stop) : IDisposable
    {
        public void Dispose() => stop();
    }
}

using System.Diagnostics;
using System.Globalization;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Umsatzschaetzung.Headless;

// What a step costs a person: how long the UI thread is busy with jobs and frames, how long
// until the app is quiet again (no UI work, no pool thread busy), and what it burned meanwhile.
public sealed class Perf(TextWriter output)
{
    const double Idle = 0.5;
    const int QuietRounds = 8;

    long start, lastBusy;
    double action, jobs, frames, longest, tails, tailsAtBusy;
    TimeSpan cpu;
    long allocated;
    int gcs;

    public void Header() =>
        output.WriteLine("step\tdo\twhat\taction_ms\tjobs_ms\tframes_ms\tlongest_ms\tquiet_ms\tcpu_ms\talloc_mb\tgc0");

    public void Begin()
    {
        start = lastBusy = Stopwatch.GetTimestamp();
        action = jobs = frames = longest = tails = tailsAtBusy = 0;
        cpu = Process.GetCurrentProcess().TotalProcessorTime;
        allocated = GC.GetTotalAllocatedBytes();
        gcs = GC.CollectionCount(0);
    }

    public void Acted()
    {
        lastBusy = Stopwatch.GetTimestamp();
        action = longest = Ms(start, lastBusy) - tails;
        tailsAtBusy = tails;
    }

    public void End(int index, string kind, string what)
    {
        output.WriteLine(string.Join('\t', index, kind, what,
            F(action), F(jobs), F(frames), F(longest), F(Ms(start, lastBusy) - tailsAtBusy),
            F((Process.GetCurrentProcess().TotalProcessorTime - cpu).TotalMilliseconds),
            ((GC.GetTotalAllocatedBytes() - allocated) / 1048576.0).ToString("0.0", CultureInfo.InvariantCulture),
            GC.CollectionCount(0) - gcs));
        output.Flush();
    }

    // Runs until the UI thread and the pool have stayed idle for a few rounds in a row. Those
    // idle rounds are the driver waiting, not the app, and are left out of every time.
    public void Settle()
    {
        var quiet = 0;
        var idleSince = Stopwatch.GetTimestamp();
        var deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60;
        while (quiet < QuietRounds && Stopwatch.GetTimestamp() < deadline)
        {
            var job = Time(() => Dispatcher.UIThread.RunJobs());
            var frame = Time(() => AvaloniaHeadlessPlatform.ForceRenderTimerTick());
            job += Time(() => Dispatcher.UIThread.RunJobs());
            jobs += job;
            frames += frame;
            longest = Math.Max(longest, Math.Max(job, frame));
            if (job + frame > Idle || PoolBusy())
            {
                quiet = 0;
                lastBusy = Stopwatch.GetTimestamp();
                tailsAtBusy = tails;
            }
            else quiet++;
            Thread.Sleep(5);
            if (quiet == 0) idleSince = Stopwatch.GetTimestamp();
        }
        tails += Ms(idleSince, Stopwatch.GetTimestamp());
    }

    static bool PoolBusy()
    {
        ThreadPool.GetAvailableThreads(out var workers, out _);
        ThreadPool.GetMaxThreads(out var max, out _);
        return max - workers > 0 || ThreadPool.PendingWorkItemCount > 0;
    }

    static double Time(Action act)
    {
        var t = Stopwatch.GetTimestamp();
        act();
        return Ms(t, Stopwatch.GetTimestamp());
    }

    static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;

    static string F(double ms) => ms.ToString("0.0", CultureInfo.InvariantCulture);
}

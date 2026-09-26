using System.Collections.Concurrent;
using Avalonia;

namespace Umsatzschaetzung.Tests.Ui;

// Avalonia sets up once per process and binds its dispatcher to that thread, so every test on the
// real desktop platform runs on this one thread, one after the other.
static class Desktop
{
    static readonly BlockingCollection<Action> Work = [];

    static Desktop()
    {
        var ui = new Thread(() =>
        {
            foreach (var run in Work.GetConsumingEnumerable()) run();
        }) { IsBackground = true, Name = "desktop" };
        if (OperatingSystem.IsWindows()) ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
    }

    public static T Run<T>(Func<T> body)
    {
        var result = new TaskCompletionSource<T>();
        Work.Add(() =>
        {
            try
            {
                if (Application.Current is null)
                    AppBuilder.Configure<App.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
                result.SetResult(body());
            }
            catch (Exception e)
            {
                result.SetException(e);
            }
        });
        return result.Task.GetAwaiter().GetResult();
    }
}

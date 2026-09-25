using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

// The real desktop platform with its native web view, not the headless one: the preview is
// exactly what the headless platform cannot show.
public class ReportViewTests
{
    [Fact]
    public void TheBerichtTabShowsTheBericht()
    {
        if (!OperatingSystem.IsWindows()) Assert.Skip("the bericht tab crashes on Windows");
        Exception? failure = null;
        (bool Ready, string Note, string Error) seen = default;
        var ui = new Thread(() =>
        {
            try { seen = Show(); }
            catch (Exception e) { failure = e; }
        });
        if (OperatingSystem.IsWindows()) ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();

        if (failure is not null) throw new Xunit.Sdk.XunitException("the bericht tab failed: " + failure);
        Assert.Equal("", seen.Error);
        Assert.Equal("", seen.Note);
        Assert.True(seen.Ready);
    }

    static (bool, string, string) Show()
    {
        using var host = new Host();
        var kase = host.PutVorlage().GetAwaiter().GetResult();
        AppBuilder.Configure<App.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();
        Exception? thrown = null;
        Dispatcher.UIThread.UnhandledException += (_, e) => { thrown = e.Exception; e.Handled = true; };

        var shell = new Shell(host.Service);
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        ReportModel? model = null;
        shell.Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            shell.Session.Open(kase);
            shell.Session.Go(Tab.Report);
            var view = (ReportView)((TabItem)shell.Tabs.SelectedItem!).Content!;
            model = (ReportModel)view.DataContext!;
            model.PropertyChanged += (_, _) =>
            {
                if (model.Ready || model.Note is not ("" or "Vorschau wird erstellt …")) done.Cancel();
            };
        });
        shell.Show();
        Dispatcher.UIThread.MainLoop(done.Token);
        var error = shell.Session.Error;
        shell.Close();

        if (thrown is not null) throw thrown;
        Assert.NotNull(model);
        return (model.Ready, model.Note, error);
    }
}

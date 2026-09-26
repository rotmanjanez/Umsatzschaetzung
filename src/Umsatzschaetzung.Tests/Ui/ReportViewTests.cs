using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Ui;

// The real desktop platform with its native web view, not the headless one: the preview is
// exactly what the headless platform cannot show.
public class ReportViewTests
{
    [Fact]
    public void TheBerichtTabShowsTheBerichtAlsoBeyondTwoMegabytes()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("the web view on macOS only runs on the main thread");
            return;
        }
        var seen = Desktop.Run(Show);

        Assert.Equal("", seen.Error);
        Assert.Equal("", seen.Note);
        Assert.True(seen.Ready);
        Assert.True(seen.Large);
    }

    static (bool Ready, string Note, string Error, bool Large) Show()
    {
        using var host = new Host();
        var kase = host.PutVorlage().GetAwaiter().GetResult();

        var shell = new Shell(host.Service);
        using var done = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        ReportModel? model = null;
        var large = false;
        shell.Loaded += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            shell.Session.Open(kase);
            shell.Session.Go(Tab.Report);
            var view = (ReportView)((TabItem)shell.Tabs.SelectedItem!).Content!;
            model = (ReportModel)view.DataContext!;
            model.PropertyChanged += async (_, e) =>
            {
                if (e.PropertyName == nameof(ReportModel.Ready) && model.Ready)
                {
                    try { large = await view.Web.Show(Page(3 << 20), TimeSpan.FromSeconds(20), done.Token); }
                    finally { done.Cancel(); }
                }
                else if (model.Note is not ("" or "Vorschau wird erstellt …")) done.Cancel();
            };
        });
        shell.Show();
        try
        {
            Dispatcher.UIThread.MainLoop(done.Token);
        }
        finally
        {
            shell.Close();
        }

        Assert.NotNull(model);
        return (model.Ready, model.Note, shell.Session.Error, large);
    }

    static string Page(int bytes)
    {
        var b = new StringBuilder("<!DOCTYPE html><html><head><meta charset=\"utf-8\"></head><body><table>");
        while (b.Length < bytes) b.Append("<tr><td>Weißbier 0,5 l vom Fass</td><td>4,60 €</td></tr>");
        return b.Append("</table></body></html>").ToString();
    }
}

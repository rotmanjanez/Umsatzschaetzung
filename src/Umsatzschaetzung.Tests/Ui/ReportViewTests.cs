using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
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
    // Installed per machine the program sits in Program Files, where the user may not write, and
    // WebView2 keeps its data next to the program unless told otherwise.
    [Fact]
    public void TheBerichtTabShowsTheBerichtOfAnInstalledProgram()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Skip("the bericht tab crashes on Windows");
            return;
        }
        using var installed = new ReadOnlyDir();
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", Path.Combine(installed.Path, "umsatzschätzung.exe.WebView2"));
        Exception? failure = null;
        (bool Ready, string Note, string Error) seen = default;
        var ui = new Thread(() =>
        {
            try { seen = Show(); }
            catch (Exception e) { failure = e; }
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", null);

        if (failure is not null) throw new Xunit.Sdk.XunitException("the bericht tab crashed: " + failure);
        Assert.Equal("", seen.Error);
        Assert.Equal("", seen.Note);
        Assert.True(seen.Ready);
    }

    static (bool, string, string) Show()
    {
        using var host = new Host();
        var kase = host.PutVorlage().GetAwaiter().GetResult();
        AppBuilder.Configure<App.App>().UsePlatformDetect().WithInterFont().SetupWithoutStarting();

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
        try
        {
            Dispatcher.UIThread.MainLoop(done.Token);
        }
        finally
        {
            shell.Close();
        }

        Assert.NotNull(model);
        return (model.Ready, model.Note, shell.Session.Error);
    }

    [SupportedOSPlatform("windows")]
    sealed class ReadOnlyDir : IDisposable
    {
        readonly DirectoryInfo dir = Directory.CreateTempSubdirectory("umsatzschätzung-programme-");
        readonly FileSystemAccessRule deny = new(WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles, AccessControlType.Deny);

        public ReadOnlyDir()
        {
            var acl = dir.GetAccessControl();
            acl.AddAccessRule(deny);
            dir.SetAccessControl(acl);
        }

        public string Path => dir.FullName;

        public void Dispose()
        {
            var acl = dir.GetAccessControl();
            acl.RemoveAccessRule(deny);
            dir.SetAccessControl(acl);
            dir.Delete(true);
        }
    }
}

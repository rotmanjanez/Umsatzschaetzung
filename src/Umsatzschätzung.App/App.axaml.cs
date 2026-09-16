using System.IO;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Umsatzschätzung.App.Platform;
using Umsatzschätzung.App.Ui;
using Umsatzschätzung.Casefile;
using Umsatzschätzung.Rulestore;
using Umsatzschätzung.Service;
using Umsatzschätzung.Tagging;

namespace Umsatzschätzung.App;

public partial class App : Application
{
    readonly List<IDisposable> owned = [];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            Start(desktop);
        base.OnFrameworkInitializationCompleted();
    }

    void Start(IClassicDesktopStyleApplicationLifetime desktop)
    {
        CrashLog.ShowError = (message, title) => ShowError(desktop, message, title);
        CrashLog.Install();
        IService service;
        try
        {
            service = CreateService();
        }
        catch (StoreUnavailableException ex)
        {
            Fatal(desktop, ex.Message, "Regelspeicher nicht verfügbar");
            return;
        }
        catch (Exception ex)
        {
            Fatal(desktop, CrashLog.Describe(ex), "Start fehlgeschlagen");
            return;
        }
        desktop.ShutdownRequested += (_, _) => { foreach (var d in owned) d.Dispose(); };
        desktop.MainWindow = new Shell(service);
    }

    static void ShowError(IClassicDesktopStyleApplicationLifetime desktop, string message, string title)
    {
        try
        {
            Dispatcher.UIThread.Post(() => Dialog.Alert(desktop.MainWindow, message, title));
        }
        catch (Exception)
        {
            Console.Error.WriteLine($"{title}: {message}");
        }
    }

    static void Fatal(IClassicDesktopStyleApplicationLifetime desktop, string message, string title)
    {
        var window = Dialog.Standalone(message, title);
        window.Closed += (_, _) => desktop.Shutdown(1);
        desktop.MainWindow = window;
    }

    IService CreateService()
    {
        var config = AppConfig.Load();
        Directory.CreateDirectory(AppData.Dir);
        var rules = new RuleStore(config.Store, Path.Combine(AppData.Dir, "snapshots"), RuleStore.Seed());
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        var ocr = new RapidOcr();
        owned.Add(ocr);
        var tagger = new Tagger();
        owned.Add(tagger);
        IPdfPages? pdf = null;
#if WINDOWS
        if (OperatingSystem.IsWindows()) pdf = new WindowsPdfPages();
#endif
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        return new LocalService(rules, new CaseStore(config.CaseDir), ocr, tagger, pdf, printer, version);
    }
}

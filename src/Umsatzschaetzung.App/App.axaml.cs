using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.App;

public partial class App : Application
{
    readonly List<IDisposable> owned = [];

    static string Version => Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";

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

    void About(object? sender, EventArgs e)
    {
        var owner = (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        _ = Dialog.Alert(owner, $"Umsatzschätzung {Version}\n© Janez Rotman", "Über Umsatzschätzung");
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
        var ocr = new RapidOcr();
        owned.Add(ocr);
        var tagger = new Tagger();
        owned.Add(tagger);
        var pdf = new PdfiumPages();
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        return new LocalService(rules, new CaseStore(config.CaseDir), ocr, tagger, pdf, printer, Version);
    }
}

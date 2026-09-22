using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.App;

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
        Config config;
        try
        {
            config = AppConfig.Load();
        }
        catch (Exception ex)
        {
            Fatal(desktop, CrashLog.Describe(ex), "Start fehlgeschlagen", false);
            return;
        }
        var instance = InstanceLock.Acquire(config.Store);
        owned.Add(instance);
        if (instance.Held)
        {
            Launch(desktop, config, false);
            return;
        }
        // Bis die Frage beantwortet ist, hängt am Programm nur dieser Dialog. Ohne
        // OnExplicitShutdown wäre mit seinem Schließen auch das Programm beendet.
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var (window, answer) = Dialog.StandaloneConfirm(
            "Umsatzschätzung läuft bereits in dieser Umgebung.\n\nZwei Fenster sehen die Regeln "
            + "getrennt voneinander: was im einen geändert wird, überschreibt das andere beim "
            + "nächsten Speichern wieder. Trotzdem ein zweites Fenster öffnen?",
            "Umsatzschätzung läuft bereits");
        desktop.MainWindow = window;
        _ = Second(desktop, config, answer);
    }

    async Task Second(IClassicDesktopStyleApplicationLifetime desktop, Config config, Task<bool> answer)
    {
        if (!await answer)
        {
            desktop.Shutdown();
            return;
        }
        desktop.ShutdownMode = ShutdownMode.OnLastWindowClose;
        Launch(desktop, config, true);
    }

    // Vor dem ersten Fenster zeigt der Lebenszyklus das MainWindow selbst, danach niemand mehr.
    void Launch(IClassicDesktopStyleApplicationLifetime desktop, Config config, bool show)
    {
        IService service;
        try
        {
            service = CreateService(config);
        }
        catch (StoreUnavailableException ex)
        {
            Fatal(desktop, ex.Message, "Datenbankfehler", show);
            return;
        }
        catch (Exception ex)
        {
            Fatal(desktop, CrashLog.Describe(ex), "Start fehlgeschlagen", show);
            return;
        }
        desktop.ShutdownRequested += (_, _) => { foreach (var d in owned) d.Dispose(); };
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        var shell = new Shell(service);
        desktop.MainWindow = shell;
        if (show) shell.Show();
    }

    public static void ShowAbout(Window? owner) =>
        _ = Dialog.Alert(owner, $"Umsatzschätzung {Release.Version}\n© Janez Rotman", "Über Umsatzschätzung");

    void About(object? sender, EventArgs e) =>
        ShowAbout((ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow);

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

    static void Fatal(IClassicDesktopStyleApplicationLifetime desktop, string message, string title, bool show)
    {
        var window = Dialog.Standalone(message, title);
        window.Closed += (_, _) => desktop.Shutdown(1);
        desktop.MainWindow = window;
        if (show) window.Show();
    }

    IService CreateService(Config config)
    {
        Directory.CreateDirectory(AppData.Dir);
        var rules = new RuleStore(config.Store, RuleStore.Seed());
        var ocr = new RapidOcr();
        owned.Add(ocr);
        _ = ocr.Warm();
        var tagger = new Tagger();
        owned.Add(tagger);
        var pdf = new PdfiumPages();
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        return new LocalService(rules, new CaseStore(config.CaseDir), ocr, tagger, pdf, printer, Release.Version);
    }
}

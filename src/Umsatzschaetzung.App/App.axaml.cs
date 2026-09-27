using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.App.Platform;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Suggest;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.App;

public partial class App : Application
{
    readonly List<IDisposable> owned = [];

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Headers.Register();
        Blur.Register();
        Cells.Register();
        Tables.Register();
        TabItems.Register();
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<UserControl>(AccessibilityView.Raw);
        AutomationProperties.ControlTypeOverrideProperty.OverrideDefaultValue<UserControl>(AutomationControlType.Group);
        AutomationProperties.AccessibilityViewProperty.OverrideDefaultValue<NativeControlHost>(AccessibilityView.Raw);
    }

    // A browser has no windows: its host composes the service and gets the program as one view.
    public static Func<Services>? SingleViewService { get; set; }

    // Nor a native web view: its host brings the Bericht's preview, or there is none.
    public static Func<IHtmlPreview>? SingleViewPreview { get; set; }

    // Nor a printer that hands back a PDF: its host opens the browser's print dialog on the Bericht.
    public static Action<string>? SingleViewPrint { get; set; }

    internal static IHtmlPreview HtmlPreview() =>
        Current?.ApplicationLifetime is ISingleViewApplicationLifetime ? SingleViewPreview?.Invoke() ?? new NoHtmlPreview() : new HtmlView();

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            Start(desktop);
        else if (ApplicationLifetime is ISingleViewApplicationLifetime single && SingleViewService is { } service)
            single.MainView = new ShellView(service());
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
        var instance = InstanceLock.Acquire(AppData.Dir);
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
        Services service;
        try
        {
            // A second instance leaves deleted invoices alone: the first can still take their deletion back.
            service = CreateService(config, purge: !show);
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

    public static void ShowAbout(Control? owner) =>
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

    Services CreateService(Config config, bool purge)
    {
        Directory.CreateDirectory(AppData.Dir);
        var rules = new RuleStore(config.Store, RuleStore.Seed());
        var weights = new OrtWeights(AppFiles.Beside("models"));
        var ocr = new RapidOcr(weights);
        owned.Add(ocr);
        _ = ocr.Warm();
        var tagger = new Tagger(weights);
        owned.Add(tagger);
        var encoder = new Encoder(weights);
        owned.Add(encoder);
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        var cases = new CaseStore(config.CaseDir);
        if (purge)
        {
            cases.Purge();
            WebPages.Clear(WebPages.Folder);
            CrashLog.Clear();
        }
        return Services.Local(rules, cases, Release.Version,
            documents: new Documents(ocr, new PdfiumPages()), tagger: tagger, ranking: new EncoderRanking(encoder, new EmbeddingStore(rules.Dir)), printer: printer);
    }
}

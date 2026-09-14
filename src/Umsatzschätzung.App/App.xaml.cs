using System.IO;
using System.Reflection;
using System.Windows;
using Umsatzschätzung.App.Platform;
using Umsatzschätzung.App.Ui;
using Umsatzschätzung.Casefile;
using Umsatzschätzung.Rulestore;
using Umsatzschätzung.Llama;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App;

public partial class App : Application
{
    readonly List<IDisposable> owned = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashLog.Install();
        DispatcherUnhandledException += (_, args) =>
        {
            CrashLog.Report(args.Exception, "Unerwarteter Fehler");
            args.Handled = true;
        };
        IService service;
        try
        {
            service = CreateService();
        }
        catch (StoreUnavailableException ex)
        {
            MessageBox.Show(ex.Message, "Regelspeicher nicht verfügbar", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }
        catch (Exception ex)
        {
            CrashLog.Report(ex, "Start fehlgeschlagen");
            Shutdown(1);
            return;
        }
        new Shell(service).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var d in owned) d.Dispose();
        base.OnExit(e);
    }

    IService CreateService()
    {
        var config = RegistryConfig.Load();
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Umsatzschätzung");
        Directory.CreateDirectory(local);
        var rules = new RuleStore(config.Store, Path.Combine(local, "snapshots"), RuleStore.Seed());
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        return new LocalService(rules, new CaseStore(config.CaseDir), new WindowsOcr(), new WindowsPdfPages(), printer, OpenModel(config), version);
    }

    ILlmEngine? OpenModel(Config config)
    {
        if (!File.Exists(Path.Combine(config.ModelDir, LlamaEngine.ModelFile))) return null;
        try
        {
            var engine = new LlamaEngine(config.ModelDir);
            owned.Add(engine);
            return engine;
        }
        catch (Exception ex)
        {
            CrashLog.Report(ex, "Sprachmodell nicht verfügbar");
            return null;
        }
    }
}

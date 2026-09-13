using System.IO;
using System.Reflection;
using System.Windows;
using Ausbeute.App.Platform;
using Ausbeute.App.Ui;
using Ausbeute.Casefile;
using Ausbeute.Rulestore;
using Ausbeute.Llama;
using Ausbeute.Service;

namespace Ausbeute.App;

public partial class App : Application
{
    readonly List<IDisposable> owned = [];

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Unerwarteter Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        new Shell(CreateService()).Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        foreach (var d in owned) d.Dispose();
        base.OnExit(e);
    }

    IService CreateService()
    {
        var config = RegistryConfig.Load();
        var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ausbeute");
        Directory.CreateDirectory(local);
        var rules = new RuleStore(config.Store, Path.Combine(local, "rules.json"));
        var printer = new WebViewPdfPrinter();
        owned.Add(printer);
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "dev";
        return new LocalService(rules, new CaseStore(config.CaseDir), new WindowsOcr(), new WindowsPdfPages(), printer, OpenModel(config), version);
    }

    ILlmEngine? OpenModel(Config config)
    {
        if (!File.Exists(Path.Combine(config.ModelDir, LlamaEngine.ModelFile))) return null;
        var engine = new LlamaEngine(config.ModelDir);
        owned.Add(engine);
        return engine;
    }
}

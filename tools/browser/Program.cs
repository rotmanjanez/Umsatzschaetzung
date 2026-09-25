using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;
using Umsatzschaetzung.App;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;

[assembly: SupportedOSPlatform("browser")]

Console.WriteLine("start");
var service = await Compose();
Console.WriteLine("composed");
App.SingleViewService = () =>
{
    Console.WriteLine("view");
    return service;
};
await AppBuilder.Configure<App>().WithInterFont().StartBrowserAppAsync("out");

// Nothing reads pixels or runs a model here: a case with one e-invoice is set up in memory.
static async Task<IService> Compose()
{
    var service = new LocalService(new RuleStore("/work/rules", RuleStore.Seed()), new CaseStore("/work/cases"), null, null, null, null, "browser");
    var ct = CancellationToken.None;
    var kase = await service.PutCase(new Case
    {
        Label = "Gasthaus Probe",
        PeriodFrom = new DateOnly(2025, 1, 1),
        PeriodTo = new DateOnly(2025, 12, 31),
        Taxpayer = new Taxpayer { Name = "Gasthaus Probe", TaxNumber = "-", PabNumber = "-" },
    }, ct);
    await using var xml = typeof(Program).Assembly.GetManifestResourceStream("rechnung.xml")!;
    var data = new byte[xml.Length];
    xml.ReadExactly(data);
    await service.ParseInvoice(kase.Id, "rechnung.xml", data, ct);
    return service;
}

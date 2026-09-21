// Kopfloser Treiber für die echte Oberfläche: ein Skript sagt, was geklickt,
// getippt, importiert und fotografiert wird. Gleicher Lauf, gleiche Bytes - eine
// Änderung im Bild heißt, die Oberfläche hat sich geändert.
//
//   dotnet run --project tools/headless -- <skript.jsonl> [--out DIR] [--rules DATEI]
//                                          [--width PT] [--height PT] [--scale N] [--pad PT]
//
//   --out     Zielordner für die Bilder (Vorgabe: neben dem Skript)
//   --rules   Regelsatz als JSON (Vorgabe: der mitgelieferte Regelsatz der App)
//   --width   Fensterbreite, --height Fensterhöhe (Vorgabe: wie die App sie öffnet)
//   --scale   Bildpunkte je Punkt (Vorgabe 2), --pad Rand um einen Ausschnitt (Vorgabe 16)
using System.Globalization;
using Avalonia;
using Avalonia.Headless;
using Umsatzschaetzung.App;
using Umsatzschaetzung.App.Ui;
using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Headless;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Tagging;

var options = new Dictionary<string, string>();
string? scriptPath = null;
for (var i = 0; i < args.Length; i++)
{
    if (!args[i].StartsWith("--")) { scriptPath = args[i]; continue; }
    if (i + 1 >= args.Length) return Usage("fehlender Wert für " + args[i]);
    options[args[i][2..]] = args[++i];
}
if (scriptPath is null) return Usage("kein Skript angegeben");

var steps = Steps.Load(scriptPath).ToList();
var outDir = options.GetValueOrDefault("out", Path.GetDirectoryName(Path.GetFullPath(scriptPath))!);
Directory.CreateDirectory(outDir);

var seed = options.TryGetValue("rules", out var rules)
    ? Json.Deserialize<RuleSet>(File.ReadAllBytes(rules))
    : RuleStore.Seed();

var work = Directory.CreateTempSubdirectory("umsatzschätzung-headless-");
var service = new LocalService(
    new RuleStore(Path.Combine(work.FullName, "store"), seed),
    new CaseStore(Path.Combine(work.FullName, "cases")),
    new RapidOcr(), new Tagger(), new PdfiumPages(), null, "headless");

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .WithInterFont()
    .SetupWithoutStarting();

var shell = new Shell(service);
if (Number("width") is { } width) shell.Width = width;
if (Number("height") is { } height) shell.Height = height;
shell.Show();
Driver.Settle();

var driver = new Driver(shell, (int)(Number("scale") ?? 2), Number("pad") ?? 16, outDir);
try
{
    foreach (var step in steps) driver.Run(step);
}
finally
{
    shell.Close();
    Directory.Delete(work.FullName, true);
}
return 0;

double? Number(string name) =>
    options.TryGetValue(name, out var text) ? double.Parse(text, CultureInfo.InvariantCulture) : null;

static int Usage(string problem)
{
    Console.Error.WriteLine(problem);
    Console.Error.WriteLine("dotnet run --project tools/headless -- <skript.jsonl> [--out DIR] [--rules DATEI]"
        + " [--width PT] [--height PT] [--scale N] [--pad PT]");
    return 2;
}

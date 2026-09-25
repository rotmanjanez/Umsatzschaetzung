// Headless driver for the real interface: a script says what is clicked, typed,
// imported and photographed. Same run, same bytes - a change in an image means the
// interface has changed.
//
//   dotnet run --project tools/headless -- <script.jsonl> [--out DIR] [--rules FILE]
//                                          [--width PT] [--height PT] [--scale N] [--pad PT]
//                                          [--readings DIR]
//
//   --out     target folder for the images (default: next to the script)
//   --rules   rule set as JSON (default: the app's own seeded rule set)
//   --width   window width, --height window height (default: as the app opens it)
//   --scale   pixels per point (default 2), --pad padding around a crop (default 16)
//   --readings folder of recorded readings: a scan read once is replayed on every later run
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
    if (i + 1 >= args.Length) return Usage("missing value for " + args[i]);
    options[args[i][2..]] = args[++i];
}
if (scriptPath is null) return Usage("no script given");

var steps = Steps.Load(scriptPath).ToList();
var outDir = options.GetValueOrDefault("out", Path.GetDirectoryName(Path.GetFullPath(scriptPath))!);
Directory.CreateDirectory(outDir);

var seed = options.TryGetValue("rules", out var rules)
    ? Json.Deserialize<RuleSet>(File.ReadAllBytes(rules))
    : RuleStore.Seed();

var work = Directory.CreateTempSubdirectory("umsatzschätzung-headless-");
var store = Path.Combine(work.FullName, "store");

AppBuilder.Configure<App>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia()
    .WithInterFont()
    .SetupWithoutStarting();

var driver = new Driver(Launch, (int)(Number("scale") ?? 2), Number("pad") ?? 16, outDir);
try
{
    foreach (var step in steps) driver.Run(step);
}
finally
{
    driver.Close();
    Directory.Delete(work.FullName, true);
}
return 0;

// A restart forgets the rule store, as if it had been deleted, and keeps the cases.
Shell Launch(bool forget)
{
    if (forget) Directory.Delete(store, true);
    var service = new LocalService(
        new RuleStore(store, seed),
        new CaseStore(Path.Combine(work.FullName, "cases")),
        new RapidOcr(), new Tagger(), new PdfiumPages(), null, "headless",
        options.TryGetValue("readings", out var readings) ? new Readings(readings) : null);
    var shell = new Shell(service);
    if (Number("width") is { } width) shell.Width = width;
    if (Number("height") is { } height) shell.Height = height;
    shell.Show();
    Driver.Settle();
    return shell;
}

double? Number(string name) =>
    options.TryGetValue(name, out var text) ? double.Parse(text, CultureInfo.InvariantCulture) : null;

static int Usage(string problem)
{
    Console.Error.WriteLine(problem);
    Console.Error.WriteLine("dotnet run --project tools/headless -- <script.jsonl> [--out DIR] [--rules FILE]"
        + " [--width PT] [--height PT] [--scale N] [--pad PT] [--readings DIR]");
    return 2;
}

using System.Text.Encodings.Web;
using System.Text.Json;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

string? seedDir = null, gewerbeSeed = null;
var pdfs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "-o" or "--seed" or "--gewerbe")
    {
        if (++i == args.Length)
        {
            Console.Error.WriteLine($"richtsatz: {args[i - 1]} braucht einen Pfad");
            return 1;
        }
        if (args[i - 1] == "--gewerbe") gewerbeSeed = args[i];
        else seedDir = args[i];
    }
    else pdfs.Add(args[i]);
}

if (pdfs.Count == 0)
{
    Console.Error.WriteLine("richtsatz [-o <seed-verzeichnis>] [--gewerbe <seed.json>] <richtsatzsammlung.pdf> ...");
    return 1;
}

if (seedDir is not null) Directory.CreateDirectory(seedDir);

var schlecht = 0;
var sammlungen = new List<Sammlung>();
foreach (var path in pdfs)
{
    try
    {
        var sammlung = Richtsätze.Read(File.ReadAllBytes(path));
        sammlungen.Add(sammlung);
        if (gewerbeSeed is not null && seedDir is null) continue;
        var target = seedDir is null
            ? Path.ChangeExtension(path, ".json")
            : Path.Combine(seedDir, sammlung.Year + ".json");
        File.WriteAllText(target, Json.Serialize(sammlung));
        Console.WriteLine($"{Path.GetFileName(path)} -> {Path.GetFileName(target)}: {sammlung.Year}, {sammlung.Klassen.Count} Gewerbeklassen, " +
                          $"{sammlung.Klassen.Sum(k => k.Staffeln.Count)} Staffeln, {sammlung.Synonyme.Count} Synonyme, " +
                          $"{sammlung.Pauschbeträge.Count} Pauschbeträge");
    }
    catch (InvalidDataException e)
    {
        Console.Error.WriteLine($"{Path.GetFileName(path)}: {e.Message}");
        schlecht++;
    }
}

if (gewerbeSeed is not null && schlecht == 0)
{
    var zweige = Gewerbezweige(sammlungen);
    File.WriteAllText(gewerbeSeed, WithGewerbe(File.ReadAllText(gewerbeSeed), zweige));
    Console.WriteLine($"{Path.GetFileName(gewerbeSeed)}: {zweige.Count} Gewerbekennzahlen aus {sammlungen.Count} Sammlungen");
}
return schlecht == 0 ? 0 : 1;

// Jede Kennzahl trägt die Bezeichnung ihrer jüngsten Sammlung; nennt diese sie unter mehreren
// Gewerbeklassen, stehen alle Namen da.
static SortedDictionary<string, string> Gewerbezweige(List<Sammlung> sammlungen)
{
    var named = new Dictionary<string, (int Year, List<string> Names)>(StringComparer.Ordinal);
    foreach (var s in sammlungen.OrderBy(s => s.Year))
        foreach (var k in s.Klassen)
            foreach (var z in k.Kennzahlen)
            {
                if (!named.TryGetValue(z, out var n) || n.Year < s.Year) named[z] = n = (s.Year, []);
                if (!n.Names.Contains(k.Name)) n.Names.Add(k.Name);
            }
    return new(named.ToDictionary(e => e.Key, e => string.Join(" / ", e.Value.Names)), StringComparer.Ordinal);
}

// Der Seed steht eine Entität je Zeile; nur der Abschnitt der Gewerbekennzahlen wird ersetzt.
static string WithGewerbe(string seed, SortedDictionary<string, string> zweige)
{
    const string section = ",\n\"gewerbezweige\": {";
    var at = seed.IndexOf(section, StringComparison.Ordinal);
    var head = at >= 0 ? seed[..at] : seed.TrimEnd()[..^1].TrimEnd();
    var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    var lines = zweige.Select(z => $"\"gw.{z.Key}\": " + JsonSerializer.Serialize(new { kennzahl = z.Key, name = z.Value }, options));
    return head + section + "\n" + string.Join(",\n", lines) + "\n}\n}\n";
}

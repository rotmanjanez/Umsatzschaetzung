using Umsatzschaetzung.Model;
using Umsatzschaetzung.Richtsatz;

string? seedDir = null;
var pdfs = new List<string>();
for (var i = 0; i < args.Length; i++)
{
    if (args[i] is "-o" or "--seed")
    {
        if (++i == args.Length)
        {
            Console.Error.WriteLine("richtsatz: -o braucht ein Verzeichnis");
            return 1;
        }
        seedDir = args[i];
    }
    else pdfs.Add(args[i]);
}

if (pdfs.Count == 0)
{
    Console.Error.WriteLine("richtsatz [-o <seed-verzeichnis>] <richtsatzsammlung.pdf> ...");
    return 1;
}

if (seedDir is not null) Directory.CreateDirectory(seedDir);

var schlecht = 0;
foreach (var path in pdfs)
{
    try
    {
        var sammlung = Richtsätze.Read(File.ReadAllBytes(path));
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
return schlecht == 0 ? 0 : 1;

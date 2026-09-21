using System.Collections.Concurrent;
using System.Diagnostics;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Ocr;

// Runs the app's own page pipeline (render, clean, straighten, OCR) over every page image
// and PDF below a directory and writes <name>.ocr.json beside each: the corpus in
// tools/corpus gets real OCR text to train on, the fixtures in fixtures/dataset/2025 get
// the dumps the eval scores.
static class Program
{
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".pdf"];

    static async Task<int> Main(string[] args)
    {
        string? root = null;
        var force = false;
        var dpi = Scan.Dpi;
        var workers = Math.Max(1, Environment.ProcessorCount / 2);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--force": force = true; break;
                case "--dpi": dpi = int.Parse(args[++i]); break;
                case "--workers": workers = int.Parse(args[++i]); break;
                case "-h" or "--help": Usage(); return 0;
                default:
                    if (args[i].StartsWith('-')) { Console.Error.WriteLine($"unbekannte Option: {args[i]}"); return 2; }
                    root = args[i];
                    break;
            }
        }
        if (root is null) { Usage(); return 2; }
        if (!Directory.Exists(root)) { Console.Error.WriteLine($"kein Verzeichnis: {root}"); return 2; }

        var todo = Walk(root, force).Order(StringComparer.Ordinal).ToList();
        Console.WriteLine($"{todo.Count} Seiten, {workers} parallel, PDF-Raster {dpi} dpi, Erkennung auf {RapidOcr.Detector}");

        var pdf = new PdfiumPages();
        var pool = new ConcurrentBag<RapidOcr>();
        var clock = Stopwatch.StartNew();
        int done = 0, failed = 0;
        long words = 0;

        await Parallel.ForEachAsync(todo, new ParallelOptions { MaxDegreeOfParallelism = workers },
            async (file, ct) =>
            {
                if (!pool.TryTake(out var ocr)) ocr = new RapidOcr();
                var started = Stopwatch.GetTimestamp();
                try
                {
                    var pages = await Scan.Read(ocr, pdf, file, await File.ReadAllBytesAsync(file, ct), dpi, ct);
                    Dump.Of(pages).Write(Dump.PathFor(file));
                    var found = pages.Sum(p => p.Words.Count);
                    Interlocked.Add(ref words, found);
                    Console.WriteLine($"[{Interlocked.Increment(ref done),6}/{todo.Count}] " +
                        $"{Path.GetRelativePath(root, file)}  {pages.Count} S.  {found,5} Wörter  " +
                        $"{Stopwatch.GetElapsedTime(started).TotalSeconds:F1}s");
                }
                catch (Exception e)
                {
                    Interlocked.Increment(ref failed);
                    Console.Error.WriteLine($"{Path.GetRelativePath(root, file)}: {e.Message}");
                }
                finally { pool.Add(ocr); }
            });

        foreach (var ocr in pool) ocr.Dispose();

        Console.WriteLine($"\n{done} Seiten, {words} Wörter, {failed} Fehler, {clock.Elapsed:hh\\:mm\\:ss}");
        return failed > 0 ? 1 : 0;
    }

    static void Usage() => Console.WriteLine(
        $"""
        corpus-ocr <verzeichnis> [--force] [--workers N] [--dpi {Scan.Dpi}]

        Liest jede .png/.jpg/.jpeg/.pdf unterhalb von <verzeichnis> wie die App
        (Raster, Reinigung, Begradigung, RapidOCR) und schreibt <name>.ocr.json
        daneben. Bereits erkannte Seiten werden übersprungen, ausser mit --force.
        --dpi ist das Raster der PDF-Seiten; der Korpus aus tools/corpus wurde mit
        --scale 3 = 288 dpi geschrieben und braucht dasselbe Raster.
        """);

    // One listing per directory, and "already done" answered out of that listing rather than a
    // File.Exists per page: over a network share the round trips cost more than the bytes. Not
    // SearchOption.AllDirectories: a shared-folder driver can hand back "." and ".." as real
    // entries and append a NUL to every name.
    static IEnumerable<string> Walk(string root, bool force)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            var pages = new List<string>();
            var dumps = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in new DirectoryInfo(dir).EnumerateFileSystemInfos())
            {
                var name = entry.Name.TrimEnd('\0');
                if (name is "." or "..") continue;
                if (entry.Attributes.HasFlag(FileAttributes.Directory)) pending.Push(Path.Combine(dir, name));
                else if (name.EndsWith(Dump.Suffix, StringComparison.OrdinalIgnoreCase)) dumps.Add(name);
                else if (Extensions.Contains(Path.GetExtension(name).ToLowerInvariant())) pages.Add(name);
            }
            foreach (var name in pages)
                if (force || !dumps.Contains(Path.GetFileNameWithoutExtension(name) + Dump.Suffix))
                    yield return Path.Combine(dir, name);
        }
    }
}

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Json;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.Ocr;

// Walks a directory, runs the app's own RapidOCR over every page image it finds and
// drops the word list next to the source as <name>.ocr.json. Gives the synthetic
// corpus in tools/corpus real OCR text to train on, and dumps the real scans in
// fixtures/dataset/2025 for the eval. PDFs go through the app's own renderer, so the
// page images are the ones the app would have read.
static class Program
{
    static readonly string[] Extensions = [".png", ".jpg", ".jpeg", ".pdf"];

    static async Task<int> Main(string[] args)
    {
        string? root = null;
        var force = false;
        var dpi = 288;
        // Each worker holds a whole page plus its own set of ONNX sessions. Half the
        // cores keeps that within reach of a normal machine.
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
        Console.WriteLine($"{todo.Count} Seiten, {workers} parallel, PDF-Raster {dpi} dpi");

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
                    var pages = await Recognize(ocr, file, dpi, ct);
                    Write(Dump(file), pages);
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
        """
        corpus-ocr <verzeichnis> [--force] [--workers N] [--dpi 288]

        Erkennt jede .png/.jpg/.jpeg/.pdf unterhalb von <verzeichnis> mit
        RapidOCR (PP-OCRv5, lateinisches Modell) und schreibt <name>.ocr.json
        daneben. Bereits erkannte Seiten werden übersprungen, ausser mit --force.
        """);

    // One listing per directory, and the "already done" check is answered out of that
    // same listing rather than a File.Exists per page: over a high-latency share the
    // round trips cost far more than the bytes.
    //
    // Not SearchOption.AllDirectories: a shared-folder driver can hand back "." and ".."
    // as real entries, which sends the built-in recursion into a loop, and can append a
    // NUL to every name, which then trips the path checks on every later File call.
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
                else if (name.EndsWith(DumpSuffix, StringComparison.OrdinalIgnoreCase)) dumps.Add(name);
                else if (Extensions.Contains(Path.GetExtension(name).ToLowerInvariant())) pages.Add(name);
            }
            foreach (var name in pages)
                if (force || !dumps.Contains(Path.GetFileNameWithoutExtension(name) + DumpSuffix))
                    yield return Path.Combine(dir, name);
        }
    }

    const string DumpSuffix = ".ocr.json";

    static string Dump(string file) =>
        Path.Combine(Path.GetDirectoryName(file) ?? ".", Path.GetFileNameWithoutExtension(file) + DumpSuffix);

    sealed record Page(int Width, int Height, bool Rotated, List<Model.OcrWord> Words);

    static async Task<List<Page>> Recognize(RapidOcr ocr, string file, int dpi, CancellationToken ct)
    {
        var bytes = await File.ReadAllBytesAsync(file, ct);
        var images = Path.GetExtension(file).Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? await Pdf.Render(bytes, dpi, ct)
            : [bytes];
        var pages = new List<Page>(images.Count);
        foreach (var image in images)
        {
            var result = await ocr.Recognize(image, ct);
            // A non-null image means the engine preferred a rotated read, so those boxes
            // sit in the rotated frame and will not line up with the render-time ones.
            pages.Add(new Page(result.Width, result.Height, result.Image is not null, result.Words));
        }
        return pages;
    }

    static readonly PdfiumPages Pdf = new();

    static readonly JsonWriterOptions Layout = new()
    {
        Indented = true,
        IndentSize = 1,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // Written to sit beside the generator's truth.json: same indent, same box shape,
    // same unescaped German, so the two read side by side.
    static void Write(string path, List<Page> pages)
    {
        var temp = path + ".tmp";
        // WriteThrough, not File.Create: the rename below is atomic, but a machine that
        // dies with the bytes still in the page cache leaves a dump that is present,
        // truncated, and — because resuming only looks for the name — never redone.
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                                           4096, FileOptions.WriteThrough))
        using (var json = new Utf8JsonWriter(stream, Layout))
        {
            json.WriteStartObject();
            json.WriteString("engine", RapidOcr.Name);
            json.WriteString("language", "de-DE");
            json.WriteNumber("maxImageDimension", RapidOcr.MaxImageDimension);
            json.WriteStartArray("pages");
            foreach (var page in pages)
            {
                json.WriteStartObject();
                json.WriteNumber("width", page.Width);
                json.WriteNumber("height", page.Height);
                if (page.Rotated) json.WriteBoolean("rotated", true);
                json.WriteStartArray("words");
                foreach (var word in page.Words)
                {
                    json.WriteStartObject();
                    json.WriteString("t", word.Text);
                    json.WriteStartArray("box");
                    json.WriteNumberValue(word.Box.X);
                    json.WriteNumberValue(word.Box.Y);
                    json.WriteNumberValue(word.Box.W);
                    json.WriteNumberValue(word.Box.H);
                    json.WriteEndArray();
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        File.Move(temp, path, overwrite: true);
    }
}

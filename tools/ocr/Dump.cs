using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Ocr;

// <name>.ocr.json beside a page image or PDF: what the app's own preprocessing and OCR read
// off it. tools/train/align.py and tools/eval read the same shape.
public sealed record Dump(string Engine, string Language, int MaxImageDimension, List<DumpPage> Pages)
{
    public const string Suffix = ".ocr.json";

    static readonly JsonSerializerOptions Layout = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        IndentSize = 1,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static string PathFor(string file) =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file) ?? ".", System.IO.Path.GetFileNameWithoutExtension(file) + Suffix);

    public static Dump Of(List<OcrPage> pages) => new(Umsatzschaetzung.Service.RapidOcr.Name, "de",
        Umsatzschaetzung.Service.RapidOcr.MaxImageDimension, [.. pages.Select(DumpPage.Of)]);

    // Written through to disk and renamed into place: a dump that is present but truncated
    // would never be redone, since resuming only looks for the name.
    public void Write(string path)
    {
        var temp = path + ".tmp";
        using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            JsonSerializer.Serialize(stream, this, Layout);
        File.Move(temp, path, overwrite: true);
    }

    public static Dump Read(string path)
    {
        using var stream = File.OpenRead(path);
        return JsonSerializer.Deserialize<Dump>(stream, Layout) ?? throw new InvalidDataException(path);
    }
}

public sealed record DumpPage(int Width, int Height, List<DumpWord> Words)
{
    public static DumpPage Of(OcrPage page) => new(page.Width, page.Height, [.. page.Words.Select(DumpWord.Of)]);

    public List<OcrWord> OcrWords() => [.. Words.Select(w => w.OcrWord())];
}

public sealed record DumpWord(string T, int[] Box, float C)
{
    public static DumpWord Of(OcrWord w) => new(w.Text, [w.Box.X, w.Box.Y, w.Box.W, w.Box.H], MathF.Round(w.Confidence, 4));

    public OcrWord OcrWord() => new() { Text = T, Box = new Box(Box[0], Box[1], Box[2], Box[3]), Confidence = C };
}

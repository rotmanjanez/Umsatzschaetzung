using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Umsatzschaetzung.Tagging;

// GPT-2 style byte-level BPE, the inference half of the GottBERT tokenizer. Ported from
// the Bpe class in tools/train/tokenizer_parity.py, which is the spec: it rebuilds the
// tokenizer from the four dumped artefacts alone and matches every case in cases.jsonl.
// A silent mismatch between training and inference does not crash, it just quietly
// costs accuracy.
public sealed class Bpe
{
    public const string FileName = "vocab.json";

    readonly Dictionary<string, int> vocab;
    readonly Dictionary<(string, string), int> ranks;
    readonly string[] bytes = new string[256];
    readonly Regex split;
    readonly ConcurrentDictionary<string, int[]> pieces = new();

    public int Unk { get; }
    public int Bos { get; }
    public int Eos { get; }
    public int Pad { get; }

    Bpe(string dir)
    {
        using var spec = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "spec.json")));
        var root = spec.RootElement;
        Unk = root.GetProperty("unk_id").GetInt32();
        var specials = root.GetProperty("specials");
        Bos = specials.GetProperty("<s>").GetInt32();
        Eos = specials.GetProperty("</s>").GetInt32();
        Pad = specials.GetProperty("<pad>").GetInt32();
        split = new Regex(root.GetProperty("pretokenizer_regex").GetString()!, RegexOptions.Compiled);

        using var v = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "vocab.json")));
        vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var token in v.RootElement.EnumerateObject()) vocab[token.Name] = token.Value.GetInt32();

        using var b = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(dir, "byte_to_unicode.json")));
        foreach (var entry in b.RootElement.EnumerateObject()) bytes[int.Parse(entry.Name)] = entry.Value.GetString()!;

        ranks = [];
        var lines = File.ReadAllText(Path.Combine(dir, "merges.txt"), Encoding.UTF8).Split('\n');
        var start = lines.Length > 0 && lines[0].StartsWith('#') ? 1 : 0;
        for (var i = start; i < lines.Length; i++)
        {
            var parts = lines[i].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2) ranks[(parts[0], parts[1])] = i - start;
        }
    }

    public static Bpe Open(string dir)
    {
        try
        {
            return new Bpe(dir);
        }
        catch (Exception e) when (e is IOException or JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                "Die Wortzerlegung für die Belegerkennung konnte nicht geladen werden. Erwartet unter " + dir + ".", e);
        }
    }

    // Every OCR word is encoded as " " + word: that is what training did, and the leading
    // space is what the GPT-2 alphabet turns into the word-start marker.
    public int[] Word(string text) => Encode(" " + text);

    public int[] Encode(string text)
    {
        var ids = new List<int>();
        foreach (Match m in split.Matches(text)) ids.AddRange(pieces.GetOrAdd(m.Value, Piece));
        return [.. ids];
    }

    int[] Piece(string piece)
    {
        var b = new StringBuilder();
        foreach (var raw in Encoding.UTF8.GetBytes(piece)) b.Append(bytes[raw]);
        var parts = Merge(b.ToString());
        var ids = new int[parts.Count];
        for (var i = 0; i < parts.Count; i++) ids[i] = vocab.TryGetValue(parts[i], out var id) ? id : Unk;
        return ids;
    }

    // One merge per pass at the leftmost lowest-ranked adjacent pair, exactly as the
    // reference does it. Words are a handful of characters, so the quadratic scan is
    // cheaper than maintaining a heap, and every distinct piece is cached anyway.
    List<string> Merge(string mapped)
    {
        var parts = new List<string>(mapped.Length);
        foreach (var c in mapped) parts.Add(c.ToString());
        while (parts.Count > 1)
        {
            var best = int.MaxValue;
            var at = -1;
            for (var i = 0; i < parts.Count - 1; i++)
            {
                if (ranks.TryGetValue((parts[i], parts[i + 1]), out var r) && r < best) (best, at) = (r, i);
            }
            if (at < 0) break;
            parts[at] += parts[at + 1];
            parts.RemoveAt(at + 1);
        }
        return parts;
    }
}

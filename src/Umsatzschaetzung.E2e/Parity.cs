using System.Text.Json;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.E2e;

// Runs the C# byte-level BPE over every case tools/train/tokenizer_parity.py dumped and
// asserts the ids are identical. A tokenizer that diverges from the one the model was
// trained with does not crash and does not error; it quietly costs accuracy.
static class TokenizerParity
{
    public static int Run(string dir, string? write)
    {
        var bpe = Bpe.Open(dir);
        var actual = write is null ? null : new StreamWriter(write);
        int n = 0, bad = 0;
        foreach (var line in File.ReadLines(Path.Combine(dir, "cases.jsonl")))
        {
            if (line.Trim() == "") continue;
            n++;
            using var doc = JsonDocument.Parse(line);
            var text = doc.RootElement.GetProperty("text").GetString()!;
            var want = doc.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var got = bpe.Word(text);
            actual?.WriteLine($"{{\"text\": {JsonSerializer.Serialize(text)}, \"ids\": [{string.Join(", ", got)}]}}");
            if (got.SequenceEqual(want)) continue;
            bad++;
            if (bad <= 10)
                Console.Error.WriteLine($"{text}: python [{string.Join(", ", want)}] != csharp [{string.Join(", ", got)}]");
        }
        actual?.Dispose();
        Console.WriteLine($"{n} cases, {bad} mismatches");
        return bad == 0 ? 0 : 1;
    }
}

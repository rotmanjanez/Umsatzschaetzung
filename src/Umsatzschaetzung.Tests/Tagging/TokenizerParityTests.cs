using System.Text.Json;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Tagging;

// A tokenizer that diverges from the one the model was trained with does not crash and
// does not error; it quietly costs accuracy. The cases are what
// tools/train/tokenizer_parity.py dump wrote: an OCR word and the ids Python gives it.
public class TokenizerParityTests
{
    static readonly string Generated = Path.Combine(TestData.Repo, "tools", "train", "tokenizer");

    static void AssertParity(Bpe bpe, string cases)
    {
        var n = 0;
        var bad = new List<string>();
        foreach (var line in File.ReadLines(cases))
        {
            if (line.Trim() == "") continue;
            n++;
            using var doc = JsonDocument.Parse(line);
            var text = doc.RootElement.GetProperty("text").GetString()!;
            var want = doc.RootElement.GetProperty("ids").EnumerateArray().Select(e => e.GetInt32()).ToArray();
            var got = bpe.Word(text);
            if (!got.SequenceEqual(want) && bad.Count < 10)
                bad.Add($"{JsonSerializer.Serialize(text)}: python [{string.Join(", ", want)}] != csharp [{string.Join(", ", got)}]");
        }
        Assert.True(n > 0, "no cases in " + cases);
        Assert.True(bad.Count == 0, $"{n} cases, mismatches:\n" + string.Join('\n', bad));
    }

    [Fact]
    public void TheShippedTokenizerMatchesPythonOnTheCheckedInCases() =>
        AssertParity(Bpe.Open(AppFiles.Beside(Path.Combine("models", "belegtagger"))), TestData.File(Path.Combine("tagging", "tokenizer-cases.jsonl")));

    [Fact]
    public void TheTokenizerMatchesPythonOnEveryWordOfTheCorpus()
    {
        if (!File.Exists(Path.Combine(Generated, "cases.jsonl")))
            Assert.Skip("tools/train/tokenizer is not generated; see tools/train/README.md");
        AssertParity(Bpe.Open(Generated), Path.Combine(Generated, "cases.jsonl"));
    }
}

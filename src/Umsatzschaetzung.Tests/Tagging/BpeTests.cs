using System.Text.Json;
using Umsatzschaetzung.Nets;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Tagging;

public sealed class BpeTests : IDisposable
{
    static readonly string Shipped = AppFiles.Beside(Path.Combine("models", "belegtagger"));
    static readonly Lazy<Task<Bpe>> Real = new(() => Bpe.Open(new OrtWeights(AppFiles.Beside("models")), "belegtagger"));

    const string Gpt2Split = @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+";

    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    // A tokenizer of a few letters: byte map as shipped, everything else hand-written.
    Task<Bpe> Tiny(string merges, string split = Gpt2Split)
    {
        var at = dir.Sub(Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(at);
        File.Copy(Path.Combine(Shipped, "byte_to_unicode.json"), Path.Combine(at, "byte_to_unicode.json"));
        File.WriteAllText(Path.Combine(at, "vocab.json"), JsonSerializer.Serialize(new Dictionary<string, int>
        {
            ["<s>"] = 0, ["<pad>"] = 1, ["</s>"] = 2, ["<unk>"] = 3,
            ["a"] = 4, ["b"] = 5, ["c"] = 6, ["ab"] = 7, ["bc"] = 8, ["abc"] = 9, ["Ġ"] = 10, ["Ġa"] = 11, ["aa"] = 12,
        }));
        File.WriteAllText(Path.Combine(at, "merges.txt"), merges);
        File.WriteAllText(Path.Combine(at, "spec.json"), JsonSerializer.Serialize(new
        {
            pretokenizer_regex = split,
            unk_id = 3,
            specials = new Dictionary<string, int> { ["<s>"] = 0, ["</s>"] = 2, ["<pad>"] = 1, ["<unk>"] = 3 },
        }));
        return Bpe.Open(new OrtWeights(dir.Path), Path.GetFileName(at));
    }

    [Fact]
    public async Task TheSpecialsComeFromTheSpec()
    {
        var bpe = await Real.Value;
        Assert.Equal((0, 2, 1, 3), (bpe.Bos, bpe.Eos, bpe.Pad, bpe.Unk));
    }

    [Theory]
    [InlineData("Rechnung")]
    [InlineData("1.234,56")]
    [InlineData("Größe")]
    [InlineData("")]
    public async Task AWordIsEncodedAfterASpace(string word)
    {
        var bpe = await Real.Value;
        Assert.Equal(bpe.Encode(" " + word), bpe.Word(word));
    }

    [Fact]
    public async Task NothingIsNoTokensButAnEmptyWordIsItsSpace()
    {
        var bpe = await Real.Value;
        Assert.Empty(bpe.Encode(""));
        Assert.Equal([751], bpe.Word(""));
    }

    [Theory]
    [InlineData("Pils 0,5 l", new[] { "Pils", " 0", ",", "5", " l" })]
    [InlineData("MwSt. 19%", new[] { "MwSt", ".", " 19", "%" })]
    [InlineData("a  b", new[] { "a", " ", " b" })]
    [InlineData("it's", new[] { "it", "'s" })]
    public async Task ATextIsEncodedPieceByPieceAsTheSpecSplitsIt(string text, string[] pieces)
    {
        var bpe = await Real.Value;
        Assert.Equal(pieces.SelectMany(bpe.Encode), bpe.Encode(text));
    }

    [Fact]
    public async Task TheSameWordGivesTheSameIdsAgain()
    {
        var bpe = await Real.Value;
        var first = bpe.Word("Sonnenallee");
        Assert.Equal(first, bpe.Word("Sonnenallee"));
        Assert.Equal([2525, 22030], first);
    }

    [Fact]
    public async Task MergesApplyInTheOrderOfTheFile()
    {
        var byLine = await Tiny("#version: 0.2\nb c\na b\nab c\n");
        var reordered = await Tiny("#version: 0.2\na b\nab c\nb c\n");
        Assert.Equal([4, 8], byLine.Encode("abc"));
        Assert.Equal([9], reordered.Encode("abc"));
    }

    [Fact]
    public async Task AMergesFileWithoutHeaderStartsAtItsFirstLine()
    {
        var bpe = await Tiny("b c\na b\n");
        Assert.Equal([4, 8], bpe.Encode("abc"));
    }

    [Fact]
    public async Task TheLeftmostOfEqualPairsMergesFirst()
    {
        var bpe = await Tiny("#version: 0.2\na a\n");
        Assert.Equal([12, 4], bpe.Encode("aaa"));
    }

    [Fact]
    public async Task ALeadingSpaceBecomesTheWordStartMarker()
    {
        var bpe = await Tiny("#version: 0.2\nĠ a\n");
        Assert.Equal([11], bpe.Word("a"));
        Assert.Equal([10], bpe.Encode(" "));
    }

    [Fact]
    public async Task WhatTheVocabularyLacksIsUnknownByteForByte()
    {
        var bpe = await Tiny("#version: 0.2\n");
        Assert.Equal([3], bpe.Encode("x"));
        Assert.Equal([3, 3], bpe.Encode("é"));
        Assert.Equal([3, 3, 3], bpe.Encode("€"));
        Assert.Equal([4, 3, 5], bpe.Encode("a\u0001b"));
    }

    [Fact]
    public async Task ThePreTokenizerIsTheSpecs()
    {
        var merges = "#version: 0.2\na b\n";
        var gpt2 = await Tiny(merges);
        var dot = await Tiny(merges, ".");
        Assert.Equal([7], gpt2.Encode("ab"));
        Assert.Equal([4, 5], dot.Encode("ab"));
    }

    [Fact]
    public async Task AMissingTokenizerSaysWhereItWasExpected()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => Bpe.Open(new OrtWeights(dir.Path), "fehlt", TestContext.Current.CancellationToken));
        Assert.Contains("fehlt", e.Message);
    }

    [Fact]
    public async Task ASpecWithoutItsSpecialsIsRefused()
    {
        var at = dir.Sub("kaputt");
        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, "spec.json"), """{"unk_id": 3}""");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Bpe.Open(new OrtWeights(dir.Path), "kaputt", TestContext.Current.CancellationToken));
    }
}

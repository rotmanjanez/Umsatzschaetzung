using System.Text.Json;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Tagging;

public sealed class BpeTests : IDisposable
{
    static readonly string Shipped = AppFiles.Beside(Path.Combine("models", "belegtagger"));
    static readonly Lazy<Bpe> Real = new(() => Bpe.Open(Shipped));

    const string Gpt2Split = @"'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+";

    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    // A tokenizer of a few letters: byte map as shipped, everything else hand-written.
    Bpe Tiny(string merges, string split = Gpt2Split)
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
        return Bpe.Open(at);
    }

    [Fact]
    public void TheSpecialsComeFromTheSpec() =>
        Assert.Equal((0, 2, 1, 3), (Real.Value.Bos, Real.Value.Eos, Real.Value.Pad, Real.Value.Unk));

    [Theory]
    [InlineData("Rechnung")]
    [InlineData("1.234,56")]
    [InlineData("Größe")]
    [InlineData("")]
    public void AWordIsEncodedAfterASpace(string word) => Assert.Equal(Real.Value.Encode(" " + word), Real.Value.Word(word));

    [Fact]
    public void NothingIsNoTokensButAnEmptyWordIsItsSpace()
    {
        Assert.Empty(Real.Value.Encode(""));
        Assert.Equal([751], Real.Value.Word(""));
    }

    [Theory]
    [InlineData("Pils 0,5 l", new[] { "Pils", " 0", ",", "5", " l" })]
    [InlineData("MwSt. 19%", new[] { "MwSt", ".", " 19", "%" })]
    [InlineData("a  b", new[] { "a", " ", " b" })]
    [InlineData("it's", new[] { "it", "'s" })]
    public void ATextIsEncodedPieceByPieceAsTheSpecSplitsIt(string text, string[] pieces) =>
        Assert.Equal(pieces.SelectMany(Real.Value.Encode), Real.Value.Encode(text));

    [Fact]
    public void TheSameWordGivesTheSameIdsAgain()
    {
        var first = Real.Value.Word("Sonnenallee");
        Assert.Equal(first, Real.Value.Word("Sonnenallee"));
        Assert.Equal([2525, 22030], first);
    }

    [Fact]
    public void MergesApplyInTheOrderOfTheFile()
    {
        Assert.Equal([4, 8], Tiny("#version: 0.2\nb c\na b\nab c\n").Encode("abc"));
        Assert.Equal([9], Tiny("#version: 0.2\na b\nab c\nb c\n").Encode("abc"));
    }

    [Fact]
    public void AMergesFileWithoutHeaderStartsAtItsFirstLine() =>
        Assert.Equal([4, 8], Tiny("b c\na b\n").Encode("abc"));

    [Fact]
    public void TheLeftmostOfEqualPairsMergesFirst() =>
        Assert.Equal([12, 4], Tiny("#version: 0.2\na a\n").Encode("aaa"));

    [Fact]
    public void ALeadingSpaceBecomesTheWordStartMarker()
    {
        var bpe = Tiny("#version: 0.2\nĠ a\n");
        Assert.Equal([11], bpe.Word("a"));
        Assert.Equal([10], bpe.Encode(" "));
    }

    [Fact]
    public void WhatTheVocabularyLacksIsUnknownByteForByte()
    {
        var bpe = Tiny("#version: 0.2\n");
        Assert.Equal([3], bpe.Encode("x"));
        Assert.Equal([3, 3], bpe.Encode("é"));
        Assert.Equal([3, 3, 3], bpe.Encode("€"));
        Assert.Equal([4, 3, 5], bpe.Encode("a\u0001b"));
    }

    [Fact]
    public void ThePreTokenizerIsTheSpecs()
    {
        var merges = "#version: 0.2\na b\n";
        Assert.Equal([7], Tiny(merges).Encode("ab"));
        Assert.Equal([4, 5], Tiny(merges, ".").Encode("ab"));
    }

    [Fact]
    public void AMissingTokenizerSaysWhereItWasExpected()
    {
        var missing = dir.Sub("fehlt");
        var e = Assert.Throws<InvalidOperationException>(() => Bpe.Open(missing));
        Assert.Contains(missing, e.Message);
    }

    [Fact]
    public void ASpecWithoutItsSpecialsIsRefused()
    {
        var at = dir.Sub("kaputt");
        Directory.CreateDirectory(at);
        File.WriteAllText(Path.Combine(at, "spec.json"), """{"unk_id": 3}""");
        Assert.Throws<InvalidOperationException>(() => Bpe.Open(at));
    }
}

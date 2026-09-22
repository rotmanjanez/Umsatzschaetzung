using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Extract;

// A tagged page built by hand: one call per printed row, 30 px apart, words 10 px a letter.
sealed class Sheet
{
    const int Height = 20;
    const int Letter = 10;
    const int Space = 8;

    public List<TaggedWord> Words { get; } = [];
    int row;

    // One cell per (x, text); the words of a cell sit a word space apart.
    public Sheet Cells(Role role, params (int X, string Text)[] cells)
    {
        foreach (var (x, text) in cells)
            Place(role, x, text, Field.Cell, 1f, cellStart: true);
        row++;
        return this;
    }

    public Sheet Line(Role role, params (int X, string Text, Field? Field)[] runs) => Line(role, 1f, runs);

    public Sheet Line(Role role, float conf, params (int X, string Text, Field? Field)[] runs)
    {
        foreach (var (x, text, field) in runs)
            Place(role, x, text, field, conf, cellStart: false);
        row++;
        return this;
    }

    void Place(Role role, int x, string text, Field? field, float conf, bool cellStart)
    {
        var first = true;
        foreach (var word in text.Split(' '))
        {
            Words.Add(new TaggedWord(Word(word, x, row * 30), field, role, row, conf, 0, cellStart && first));
            x += word.Length * Letter + Space;
            first = false;
        }
    }

    public static OcrWord Word(string text, int x, int y) =>
        new() { Text = text, Box = new Box(x, y, text.Length * Letter, Height) };
}

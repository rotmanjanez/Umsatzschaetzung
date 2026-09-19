using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Extract;

internal sealed class Row(OcrWord first)
{
    public List<OcrWord> Words { get; } = [first];
    public Box Anchor { get; } = first.Box;
    public Box Box { get; set; } = first.Box;
}

internal static class Rows
{
    public static List<Row> GroupRows(IEnumerable<OcrWord> words)
    {
        var rows = new List<Row>();
        foreach (var w in words.Where(w => w.Text.Trim() != "").OrderBy(w => w.Box.Y))
        {
            var row = rows.Find(r => OverlapsVertically(r.Anchor, w.Box));
            if (row is null)
            {
                rows.Add(new Row(w));
                continue;
            }
            row.Words.Add(w);
            row.Box = Union(row.Box, w.Box);
        }
        foreach (var r in rows)
        {
            var sorted = r.Words.OrderBy(w => w.Box.X).ToList();
            r.Words.Clear();
            r.Words.AddRange(sorted);
        }
        return rows.OrderBy(r => r.Box.Y).ToList();
    }

    static bool OverlapsVertically(Box a, Box b)
    {
        var ca = a.Y + a.H / 2;
        var cb = b.Y + b.H / 2;
        return (ca >= b.Y && ca < b.Y + b.H) || (cb >= a.Y && cb < a.Y + a.H);
    }

    public static Box Union(Box a, Box b)
    {
        if (a.W == 0 && a.H == 0) return b;
        var x = Math.Min(a.X, b.X);
        var y = Math.Min(a.Y, b.Y);
        return new Box(x, y, Math.Max(a.X + a.W, b.X + b.W) - x, Math.Max(a.Y + a.H, b.Y + b.H) - y);
    }
}

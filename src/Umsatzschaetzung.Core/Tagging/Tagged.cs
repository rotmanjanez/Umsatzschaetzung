using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tagging;

// Per word of a page a field, a table column and a cell start, per row a role.
public interface ITagger
{
    string Model { get; }
    List<TaggedWord> Tag(IReadOnlyList<OcrWord> words, int width, int height);
}

// In the order of ROLES in tools/train/schema.py: the order is part of the model contract.
public enum Role
{
    Header,
    ColumnHeader,
    LineItem,
    LineWrap,
    Continuation,
    Total,
    Footer,
    Group,
    Carry,
}

// Conf is the winning class's softmax probability; Col the model's column index inside an
// item table, 0 outside one; CellStart marks the first word of a table cell.
public sealed record TaggedWord(OcrWord Word, Field? Field, Role Role, int Row, float Conf = 1f,
    int Col = 0, bool CellStart = false);

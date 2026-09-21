using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tagging;

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

// LiLT layout stream with GottBERT as its text side, int8 ONNX. One page of OCR words in;
// per word a class, a column and a cell start, per row a role. docs/models.md §2.
public sealed class Tagger : IDisposable
{
    public const string Name = "belegtagger-2.0.0/int8";

    // STRUCT_LABELS in tools/train/schema.py: the word head's classes, in order.
    static readonly Field?[] Classes =
    [
        null, Field.Cell, Field.InvoiceNumber, Field.InvoiceDate, Field.Supplier, Field.NetTotal,
        Field.GrossTotal, Field.Vat, Field.NumberLabel, Field.DateLabel, Field.NetLabel, Field.GrossLabel,
        Field.VatLabel, Field.OtherLabel,
    ];

    // tools/train/data.py and model.py.
    const int MaxLen = 512;
    const int Overlap = 128;
    const int MaxRows = 96;
    const int BoxBins = 1024;
    const int Roles = 9;
    const int CellClasses = 2;

    // Eight threads measured 2.3x slower than four on an M1 Pro (efficiency cores).
    const int Threads = 4;

    static string Dir => AppFiles.Beside(Path.Combine("models", "belegtagger"));
    static string ModelPath => Path.Combine(Dir, "belegtagger.int8.onnx");

    readonly Lock gate = new();
    // Not InferenceSession: naming it in Dispose loads OnnxRuntime on every shutdown.
    IDisposable? session;
    Bpe? bpe;

    public void Dispose() => session?.Dispose();

    public List<TaggedWord> Tag(IReadOnlyList<OcrWord> words, int width, int height)
    {
        var rows = Rows.GroupRows(words);
        var ordered = new List<(OcrWord Word, int Row)>();
        for (var r = 0; r < rows.Count; r++)
            foreach (var w in rows[r].Words) ordered.Add((w, r));
        return Tag(ordered, width, height);
    }

    // Rows given, for the eval's word-for-word comparison with a Python dump.
    public List<TaggedWord> Tag(IReadOnlyList<(OcrWord Word, int Row)> ordered, int width, int height)
    {
        lock (gate)
        {
            bpe ??= Bpe.Open(Dir);
            session ??= Open();
            return Run((InferenceSession)session, bpe, ordered, width, height);
        }
    }

    static InferenceSession Open()
    {
        var options = new SessionOptions { IntraOpNumThreads = Threads, InterOpNumThreads = 1 };
        try
        {
            var session = new InferenceSession(ModelPath, options);
            Check(session, "word_logits", Classes.Length);
            Check(session, "role_logits", Roles);
            Check(session, "cell_logits", CellClasses);
            return session;
        }
        catch (Exception e)
        {
            options.Dispose();
            throw new InvalidOperationException(
                "Das Modell zur Belegerkennung konnte nicht geladen werden. Erwartet unter " + ModelPath + ".", e);
        }
    }

    static void Check(InferenceSession session, string output, int width)
    {
        if (Width(session, output) != width)
            throw new InvalidOperationException($"Das Belegerkennungsmodell liefert \"{output}\" nicht {width}-fach.");
    }

    static int Width(InferenceSession session, string output)
    {
        if (!session.OutputMetadata.TryGetValue(output, out var meta))
            throw new InvalidOperationException($"Das Belegerkennungsmodell liefert keinen Ausgang \"{output}\".");
        var n = meta.Dimensions[^1];
        if (n <= 0) throw new InvalidOperationException($"Das Belegerkennungsmodell gibt die Breite von \"{output}\" nicht an.");
        return n;
    }

    sealed class Logits(int tokens, int width)
    {
        public readonly int Width = width;
        public readonly float[] Sum = new float[tokens * width];
    }

    static List<TaggedWord> Run(InferenceSession session, Bpe bpe, IReadOnlyList<(OcrWord Word, int Row)> ordered,
        int width, int height)
    {
        var ids = new List<int>();
        var boxes = new List<int[]>();
        var rowOf = new List<int>();
        var starts = new List<int>();
        var kept = new List<(OcrWord Word, int Row)>();
        foreach (var (w, row) in ordered)
        {
            var sub = bpe.Word(w.Text);
            if (sub.Length == 0) continue;
            var box = Quantise(w.Box, width, height);
            starts.Add(ids.Count);
            kept.Add((w, row));
            for (var k = 0; k < sub.Length; k++)
            {
                ids.Add(sub[k]);
                boxes.Add(box);
                rowOf.Add(k == 0 ? row : -1);
            }
        }
        if (kept.Count == 0) return [];

        // Windows are summed, not averaged: every class at a position shares the window count.
        var word = new Logits(ids.Count, Classes.Length);
        var col = new Logits(ids.Count, Width(session, "col_logits"));
        var cell = new Logits(ids.Count, CellClasses);
        var role = new Dictionary<int, float[]>();

        var body = MaxLen - 2;
        var step = Math.Max(1, body - Overlap);
        for (var s = 0; s < ids.Count; s += step)
        {
            var e = Math.Min(s + body, ids.Count);
            Window(session, bpe, ids, boxes, rowOf, s, e, word, col, cell, role);
            if (e == ids.Count) break;
        }

        var tagged = new List<TaggedWord>(kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            var at = starts[i];
            var label = ArgMax(word, at);
            tagged.Add(new TaggedWord(
                kept[i].Word,
                Classes[label],
                role.TryGetValue(kept[i].Row, out var acc) ? (Role)ArgMax(acc, 0, Roles) : Role.LineItem,
                kept[i].Row,
                Softmax(word, at, label),
                ArgMax(col, at),
                ArgMax(cell, at) == 1));
        }
        return tagged;
    }

    static void Window(InferenceSession session, Bpe bpe, List<int> ids, List<int[]> boxes, List<int> rowOf,
                       int from, int to, Logits word, Logits col, Logits cell, Dictionary<int, float[]> role)
    {
        var n = to - from + 2;
        var input = new DenseTensor<long>([1, n]);
        var bbox = new DenseTensor<long>([1, n, 4]);
        var mask = new DenseTensor<long>([1, n]);
        input[0, 0] = bpe.Bos;
        input[0, n - 1] = bpe.Eos;
        for (var i = 0; i < n; i++) mask[0, i] = 1;
        for (var i = from; i < to; i++)
        {
            var at = i - from + 1;
            input[0, at] = ids[i];
            for (var c = 0; c < 4; c++) bbox[0, at, c] = boxes[i][c];
        }

        using var results = session.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", input),
            NamedOnnxValue.CreateFromTensor("bbox", bbox),
            NamedOnnxValue.CreateFromTensor("attention_mask", mask),
        ]);
        Add(Output(results, "word_logits"), from, to, word);
        Add(Output(results, "col_logits"), from, to, col);
        Add(Output(results, "cell_logits"), from, to, cell);
        var roleLogits = Output(results, "role_logits");

        // The role head is affine, so the mean of its logits over a row's first subwords is
        // the row_pool the model was trained with.
        var seen = new List<int>();
        var members = new Dictionary<int, List<int>>();
        for (var i = from; i < to; i++)
        {
            var r = rowOf[i];
            if (r < 0) continue;
            if (!members.TryGetValue(r, out var list))
            {
                if (seen.Count == MaxRows) continue;
                seen.Add(r);
                members[r] = list = [];
            }
            list.Add(i - from + 1);
        }
        foreach (var r in seen)
        {
            if (!role.TryGetValue(r, out var acc)) role[r] = acc = new float[Roles];
            var list = members[r];
            foreach (var at in list)
                for (var c = 0; c < Roles; c++) acc[c] += roleLogits[at * Roles + c] / list.Count;
        }
    }

    static void Add(float[] logits, int from, int to, Logits acc)
    {
        var w = acc.Width;
        for (var i = from; i < to; i++)
        {
            var at = (i - from + 1) * w;
            for (var c = 0; c < w; c++) acc.Sum[i * w + c] += logits[at + c];
        }
    }

    static float[] Output(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        foreach (var r in results)
            if (r.Name == name) return r.AsTensor<float>().ToArray();
        throw new InvalidOperationException($"Das Belegerkennungsmodell liefert keinen Ausgang \"{name}\".");
    }

    // model.py quantise_box: pixels to bin space, so the dpi of the scan does not matter.
    static int[] Quantise(Box box, int width, int height)
    {
        var x0 = Bin(box.X, width);
        var y0 = Bin(box.Y, height);
        return [x0, y0, Math.Max(x0, Bin(box.X + box.W, width)), Math.Max(y0, Bin(box.Y + box.H, height))];
    }

    static int Bin(int v, int size) =>
        Math.Min(BoxBins - 1, Math.Max(0, (int)(v * (double)(BoxBins - 1) / Math.Max(size, 1))));

    static int ArgMax(Logits acc, int token) => ArgMax(acc.Sum, token * acc.Width, acc.Width);

    static int ArgMax(float[] values, int offset, int count)
    {
        var best = 0;
        for (var i = 1; i < count; i++)
            if (values[offset + i] > values[offset + best]) best = i;
        return best;
    }

    static float Softmax(Logits acc, int token, int index)
    {
        var offset = token * acc.Width;
        var max = acc.Sum[offset + ArgMax(acc, token)];
        double sum = 0;
        for (var i = 0; i < acc.Width; i++) sum += Math.Exp(acc.Sum[offset + i] - max);
        return (float)(Math.Exp(acc.Sum[offset + index] - max) / sum);
    }
}

using Umsatzschaetzung.Extract;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Nets;

namespace Umsatzschaetzung.Tagging;

// LiLT layout stream with GottBERT as its text side, int8 ONNX. One page of OCR words in;
// per word a class, a column and a cell start, per row a role. docs/models.md §2.
public sealed class Tagger(IWeights weights) : ITagger, IDisposable
{
    public const string Name = "belegtagger-2.0.0/int8";

    public string Model => Name;

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

    const string Dir = "belegtagger";
    const string ModelFile = Dir + "/belegtagger.int8.onnx";

    // Eight threads measured 2.3x slower than four on an M1 Pro (efficiency cores). A thread
    // spinning idle takes its core from the reader, which reads the next scan meanwhile.
    static readonly NetOptions Options = new(Threads: 4, Spin: false);

    readonly SemaphoreSlim gate = new(1, 1);
    INet? net;
    Bpe? bpe;

    public void Dispose() => net?.Dispose();

    public Task<List<TaggedWord>> Tag(IReadOnlyList<OcrWord> words, int width, int height, CancellationToken ct = default)
    {
        var rows = Rows.GroupRows(words);
        var ordered = new List<(OcrWord Word, int Row)>();
        for (var r = 0; r < rows.Count; r++)
            foreach (var w in rows[r].Words) ordered.Add((w, r));
        return Tag(ordered, width, height, ct);
    }

    // Rows given, for the eval's word-for-word comparison with a Python dump.
    public async Task<List<TaggedWord>> Tag(IReadOnlyList<(OcrWord Word, int Row)> ordered, int width, int height, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            bpe ??= await Bpe.Open(weights, Dir, ct);
            net ??= await Open(ct);
            return await Run(net, bpe, ordered, width, height, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    async Task<INet> Open(CancellationToken ct)
    {
        INet? opened = null;
        try
        {
            opened = await weights.Open(ModelFile, Options, ct);
            Check(opened, "word_logits", Classes.Length);
            Check(opened, "role_logits", Roles);
            Check(opened, "cell_logits", CellClasses);
            return opened;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            opened?.Dispose();
            throw new InvalidOperationException(
                "Das Modell zur Belegerkennung konnte nicht geladen werden. Erwartet unter models/" + ModelFile + ": " + e.Message, e);
        }
    }

    static void Check(INet net, string output, int width)
    {
        if (Width(net, output) != width)
            throw new InvalidOperationException($"Das Belegerkennungsmodell liefert \"{output}\" nicht {width}-fach.");
    }

    static int Width(INet net, string output)
    {
        var port = net.Outputs.FirstOrDefault(p => p.Name == output)
            ?? throw new InvalidOperationException($"Das Belegerkennungsmodell liefert keinen Ausgang \"{output}\".");
        var n = port.Shape[^1];
        if (n <= 0) throw new InvalidOperationException($"Das Belegerkennungsmodell gibt die Breite von \"{output}\" nicht an.");
        return n;
    }

    sealed class Logits(int tokens, int width)
    {
        public readonly int Width = width;
        public readonly float[] Sum = new float[tokens * width];
    }

    static async Task<List<TaggedWord>> Run(INet net, Bpe bpe, IReadOnlyList<(OcrWord Word, int Row)> ordered,
        int width, int height, CancellationToken ct)
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
        var col = new Logits(ids.Count, Width(net, "col_logits"));
        var cell = new Logits(ids.Count, CellClasses);
        var role = new Dictionary<int, float[]>();

        var body = MaxLen - 2;
        var step = Math.Max(1, body - Overlap);
        for (var s = 0; s < ids.Count; s += step)
        {
            var e = Math.Min(s + body, ids.Count);
            await Window(net, bpe, ids, boxes, rowOf, s, e, word, col, cell, role, ct);
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

    static async Task Window(INet net, Bpe bpe, List<int> ids, List<int[]> boxes, List<int> rowOf,
                       int from, int to, Logits word, Logits col, Logits cell, Dictionary<int, float[]> role, CancellationToken ct)
    {
        var n = to - from + 2;
        var input = new long[n];
        var bbox = new long[n * 4];
        var mask = new long[n];
        input[0] = bpe.Bos;
        input[n - 1] = bpe.Eos;
        Array.Fill(mask, 1);
        for (var i = from; i < to; i++)
        {
            var at = i - from + 1;
            input[at] = ids[i];
            for (var c = 0; c < 4; c++) bbox[at * 4 + c] = boxes[i][c];
        }

        var results = await net.Run([
            Tensor.Of("input_ids", input, 1, n),
            Tensor.Of("bbox", bbox, 1, n, 4),
            Tensor.Of("attention_mask", mask, 1, n),
        ], ct);
        Add(results.Named("word_logits").F, from, to, word);
        Add(results.Named("col_logits").F, from, to, col);
        Add(results.Named("cell_logits").F, from, to, cell);
        var roleLogits = results.Named("role_logits").F;

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

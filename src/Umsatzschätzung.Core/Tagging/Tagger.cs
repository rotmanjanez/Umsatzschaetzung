using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Umsatzschätzung.Extract;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Tagging;

// Nine row roles, in the order of ROLES in tools/train/schema.py. The ONNX role head
// emits one logit per entry in that list, so the order is part of the model contract.
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

public sealed record TaggedWord(OcrWord Word, Field? Field, Role Role, int Row);

// Model B: the LiLT layout stream with GottBERT as its text side, exported to int8 ONNX.
// One page of OCR words in, one label and one row role per word out. docs/models.md §2.
public sealed class Tagger : IDisposable
{
    public const string Name = "lilt-gottbert-b/int8";

    // tools/train/data.py: 512 subwords per window, 128 of overlap, at most 96 rows
    // pooled per window. model.py quantises boxes into 1024 bins, not 1000.
    const int MaxLen = 512;
    const int Overlap = 128;
    const int MaxRows = 96;
    const int BoxBins = 1024;
    const int Labels = 13;
    const int Roles = 9;

    // Eight threads measured 2.3x slower than four on an M1 Pro: the scheduler puts the
    // extra work on efficiency cores. The default is not good enough here.
    const int Threads = 4;

    static string Dir => Path.Combine(AppContext.BaseDirectory, "models", "b");
    static string ModelPath => Path.Combine(Dir, "tagger.int8.onnx");

    readonly Lock gate = new();
    InferenceSession? session;
    Bpe? bpe;

    public void Dispose() => session?.Dispose();

    public List<TaggedWord> Tag(IReadOnlyList<OcrWord> words, int width, int height)
    {
        lock (gate)
        {
            bpe ??= Bpe.Open(Dir);
            session ??= Open();
            return Run(session, bpe, words, width, height);
        }
    }

    static InferenceSession Open()
    {
        var options = new SessionOptions { IntraOpNumThreads = Threads, InterOpNumThreads = 1 };
        try
        {
            return new InferenceSession(ModelPath, options);
        }
        catch (Exception e)
        {
            options.Dispose();
            throw new InvalidOperationException(
                "Das Modell zur Belegerkennung konnte nicht geladen werden. Erwartet unter " + ModelPath + ".", e);
        }
    }

    static List<TaggedWord> Run(InferenceSession session, Bpe bpe, IReadOnlyList<OcrWord> words, int width, int height)
    {
        var rows = Rows.GroupRows(words);
        var ordered = new List<(OcrWord Word, int Row)>();
        for (var r = 0; r < rows.Count; r++)
            foreach (var w in rows[r].Words) ordered.Add((w, r));

        var ids = new List<int>();
        var boxes = new List<int[]>();
        var rowOf = new List<int>();
        var starts = new List<int>();
        var kept = new List<(OcrWord Word, int Row)>();
        foreach (var (word, row) in ordered)
        {
            var sub = bpe.Word(word.Text);
            if (sub.Length == 0) continue;
            var box = Quantise(word.Box, width, height);
            starts.Add(ids.Count);
            kept.Add((word, row));
            for (var k = 0; k < sub.Length; k++)
            {
                ids.Add(sub[k]);
                boxes.Add(box);
                rowOf.Add(k == 0 ? row : -1);
            }
        }
        if (kept.Count == 0) return [];

        // Overlapping windows are summed rather than averaged: every class at one
        // position shares the same window count, so argmax is unaffected.
        var wordAcc = new float[ids.Count * Labels];
        var role = new Dictionary<int, float[]>();

        var body = MaxLen - 2;
        var step = Math.Max(1, body - Overlap);
        for (var s = 0; s < ids.Count; s += step)
        {
            var e = Math.Min(s + body, ids.Count);
            Window(session, bpe, ids, boxes, rowOf, s, e, wordAcc, role);
            if (e == ids.Count) break;
        }

        var tagged = new List<TaggedWord>(kept.Count);
        for (var i = 0; i < kept.Count; i++)
        {
            var label = ArgMax(wordAcc, starts[i] * Labels, Labels);
            tagged.Add(new TaggedWord(
                kept[i].Word,
                label == 0 ? null : (Field)(label - 1),
                role.TryGetValue(kept[i].Row, out var acc) ? (Role)ArgMax(acc, 0, Roles) : Role.LineItem,
                kept[i].Row));
        }
        return tagged;
    }

    static void Window(InferenceSession session, Bpe bpe, List<int> ids, List<int[]> boxes, List<int> rowOf,
                       int from, int to, float[] wordAcc, Dictionary<int, float[]> role)
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
        var wordLogits = Output(results, "word_logits");
        var roleLogits = Output(results, "role_logits");

        for (var i = from; i < to; i++)
        {
            var at = (i - from + 1) * Labels;
            for (var c = 0; c < Labels; c++) wordAcc[i * Labels + c] += wordLogits[at + c];
        }

        // The exported role head runs per token; it is affine, so averaging its logits over
        // a row's first subwords is exactly the row_pool mean-pool the model was trained
        // with. tools/train/data.py caps a window at 96 pooled rows.
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

    static float[] Output(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results, string name)
    {
        foreach (var r in results)
            if (r.Name == name) return r.AsTensor<float>().ToArray();
        throw new InvalidOperationException($"Das Belegerkennungsmodell liefert keinen Ausgang \"{name}\".");
    }

    // tools/train/model.py quantise_box: pixels to LiLT bin space, clamped inside the page,
    // so the same invoice scanned at 200 and at 400 dpi produces identical position inputs.
    static int[] Quantise(Box box, int width, int height)
    {
        var x0 = Bin(box.X, width);
        var y0 = Bin(box.Y, height);
        return [x0, y0, Math.Max(x0, Bin(box.X + box.W, width)), Math.Max(y0, Bin(box.Y + box.H, height))];
    }

    static int Bin(int v, int size) =>
        Math.Min(BoxBins - 1, Math.Max(0, (int)(v * (double)(BoxBins - 1) / Math.Max(size, 1))));

    static int ArgMax(float[] values, int offset, int count)
    {
        var best = 0;
        for (var i = 1; i < count; i++)
            if (values[offset + i] > values[offset + best]) best = i;
        return best;
    }
}

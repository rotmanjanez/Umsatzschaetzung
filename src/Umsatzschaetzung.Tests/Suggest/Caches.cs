using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public sealed class MemoryCache : IEmbeddingCache
{
    readonly Dictionary<(string, string), float[]> rows = [];

    public int Writes { get; private set; }

    public Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts)
    {
        var found = new Dictionary<string, float[]>(StringComparer.Ordinal);
        foreach (var t in texts)
            if (rows.TryGetValue((model, t), out var v)) found[t] = v;
        return found;
    }

    public void Write(string model, IReadOnlyList<(string Text, float[] Vec)> written)
    {
        foreach (var (t, v) in written) rows[(model, t)] = v;
        Writes += written.Count;
    }
}

// Every text the matcher asks for is known, so the model never runs and every cosine is
// the one the test chose.
sealed class FixedCache : IEmbeddingCache
{
    readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
    int next = 2;

    public FixedCache At(string text, double cos)
    {
        vectors[text] = Vec.At(cos);
        return this;
    }

    public FixedCache Query(string text) => At(text, 1);

    public FixedCache Apart(params string[] texts)
    {
        foreach (var t in texts) vectors[t] = Vec.Axis(next++);
        return this;
    }

    public Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts)
    {
        Assert.Equal(Encoder.Name, model);
        return texts.Where(vectors.ContainsKey).ToDictionary(t => t, t => vectors[t], StringComparer.Ordinal);
    }

    public void Write(string model, IReadOnlyList<(string Text, float[] Vec)> rows) =>
        Assert.Fail("the encoder ran for " + string.Join(", ", rows.Select(r => $"\"{r.Text}\"")));
}

static class Vec
{
    public static float[] At(double cos)
    {
        var v = new float[Encoder.Width];
        v[0] = (float)cos;
        v[1] = (float)Math.Sqrt(1 - cos * cos);
        return v;
    }

    public static float[] Axis(int k)
    {
        var v = new float[Encoder.Width];
        v[k] = 1;
        return v;
    }

    public static double Dot(float[] a, float[] b)
    {
        var sum = 0.0;
        for (var i = 0; i < a.Length; i++) sum += a[i] * b[i];
        return sum;
    }
}

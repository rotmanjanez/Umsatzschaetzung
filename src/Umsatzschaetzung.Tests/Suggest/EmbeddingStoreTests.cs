using Microsoft.Data.Sqlite;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.Tests.Suggest;

public sealed class EmbeddingStoreTests : IDisposable
{
    const string Model = Encoder.Name;

    readonly TempDir dir = new();

    public void Dispose() => dir.Dispose();

    static float[] V(float seed)
    {
        var v = new float[IEncoder.Width];
        for (var i = 0; i < v.Length; i++) v[i] = seed + i * 1e-3f;
        return v;
    }

    [Fact]
    public void WhatWasWrittenIsReadBackBitForBit()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, [("Fassbier Pils", V(1)), ("Doppelkorn", V(-2))]);
        var got = store.Read(Model, ["Fassbier Pils", "Doppelkorn"]);
        Assert.Equal(V(1), got["Fassbier Pils"]);
        Assert.Equal(V(-2), got["Doppelkorn"]);
    }

    [Fact]
    public void AMissIsAbsentNotEmpty()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, [("Pils", V(1))]);
        var got = store.Read(Model, ["Pils", "Weizen", "pils", "Pils "]);
        Assert.Equal(["Pils"], got.Keys);
    }

    [Fact]
    public void TheModelIsPartOfTheKey()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, [("Pils", V(1))]);
        store.Write("zuordnung-0.0.9/int8", [("Pils", V(2))]);
        Assert.Equal(V(1), store.Read(Model, ["Pils"])["Pils"]);
        Assert.Equal(V(2), store.Read("zuordnung-0.0.9/int8", ["Pils"])["Pils"]);
        Assert.Empty(store.Read("anderes", ["Pils"]));
    }

    [Fact]
    public void ALaterWriteReplacesTheVector()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, [("Pils", V(1))]);
        store.Write(Model, [("Pils", V(3))]);
        Assert.Equal(V(3), store.Read(Model, ["Pils"])["Pils"]);
    }

    [Fact]
    public void TheCacheOutlivesTheStoreThatWroteIt()
    {
        var at = dir.Sub(Path.Combine("regeln", "neu"));
        new EmbeddingStore(at).Write(Model, [("Pils", V(1))]);
        Assert.True(File.Exists(Path.Combine(at, "embeddings.db")));
        Assert.Equal(V(1), new EmbeddingStore(at).Read(Model, ["Pils"])["Pils"]);
    }

    [Fact]
    public void AVectorOfAnotherWidthIsStaleAndReadAsAMiss()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, [("kurz", new float[384]), ("gut", V(1))]);
        using (var db = new SqliteConnection($"Data Source={Path.Combine(dir.Path, "embeddings.db")};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "INSERT INTO embedding(text, model, vec) VALUES('leer', @m, x'')";
            cmd.Parameters.AddWithValue("@m", Model);
            cmd.ExecuteNonQuery();
        }
        Assert.Equal(["gut"], store.Read(Model, ["kurz", "gut", "leer"]).Keys);
    }

    [Fact]
    public void NothingAskedNothingWritten()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, []);
        Assert.Empty(store.Read(Model, []));
    }

    [Fact]
    public void AMatcherTakesItsVectorsFromTheStore()
    {
        var store = new EmbeddingStore(dir.Path);
        store.Write(Model, [("Ware", Vec.At(1)), ("Pils", Vec.At(0.9))]);
        var rs = new RuleSet();
        rs.Put(new Ingredient { Id = "ing.pils", Name = "Pils" });
        var m = new Matcher(Encoders.Shipped, store);
        var only = Assert.Single(m.Suggest(rs, "", null, new InvoiceLine { Name = "Ware" }));
        Assert.Equal(("ing.pils", Encoders.Shipped.Confidence(0.9f)), (only.Mapping.IngredientId, only.Confidence));
    }
}

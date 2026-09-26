using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Suggest;

public sealed record Suggestion(ArticleMapping Mapping, int Confidence, OriginKind Kind);

// Embedding the whole catalog costs a model run per wording, so the index's vectors are kept.
// Only the index's: an invoice line is looked up but never written, so nothing of a case reaches the shared cache.
public interface IEmbeddingCache
{
    Dictionary<string, float[]> Read(string model, IReadOnlyCollection<string> texts);
    void Write(string model, IReadOnlyList<(string Text, float[] Vec)> rows);
}

// Turns article wordings into vectors whose dot product says how alike they read.
public interface IEncoder
{
    const int Width = 768;

    string Model { get; }
    float[][] Embed(IReadOnlyList<string> texts);
    int Confidence(double cos);
}

// Without a ranking only an exact hit is known.
public sealed class Matcher(IRanking? ranking)
{
    const int Candidates = 5;
    const int Floor = 20;

    public Matcher(IEncoder? encoder, IEmbeddingCache? cache = null) : this(encoder is null ? null : new EncoderRanking(encoder, cache)) { }

    // An exact hit leads, and the ranking's alternatives follow it: revising a mapped
    // line needs them as much as an open line does.
    public List<Suggestion> Suggest(RuleSet rs, string gewerbe, string? supplier, InvoiceLine line, DateOnly? date = null)
    {
        var on = date ?? DateOnly.FromDateTime(DateTime.Now);
        var hit = Match.Mapping(rs, supplier, on, line);
        var pack = PackSize.Read(line.Name);
        var sugs = (ranking?.Rank(rs, gewerbe, line, on, Candidates + 1) ?? [])
            .Where(r => r.IngredientId != hit?.IngredientId && rs.Ingredients.ContainsKey(r.IngredientId))
            .Take(Candidates)
            .Where(r => r.Confidence >= Floor)
            .Select(r => new Suggestion(
                Mapping(supplier, line, r.IngredientId, Packed(rs, rs.Ingredients[r.IngredientId], line.UnitCode, pack)),
                r.Confidence,
                OriginKind.Encoder))
            .ToList();
        if (hit is not null) sugs.Insert(0, new Suggestion(hit, 100, OriginKind.Exact));
        return sugs;
    }

    // Invoices shout, catalogues do not, and the encoder learnt from catalogues: a
    // wording without a lower-case letter is read as if it were written in title case.
    // Measured on 600 labelled rows, upper case keeps 9 % of the auto-mappings, title
    // case 39 % of the 41 % the original spelling gets.
    public static string Normal(string text)
    {
        if (text.Any(char.IsLower)) return text;
        var b = new System.Text.StringBuilder(text.Length);
        var start = true;
        foreach (var c in text)
        {
            b.Append(start ? c : char.ToLowerInvariant(c));
            start = !char.IsLetter(c);
        }
        return b.ToString();
    }

    public static string? Wording(ArticleMapping m) =>
        !string.IsNullOrEmpty(m.Observed) ? m.Observed : !string.IsNullOrEmpty(m.Name) ? m.Name : null;

    static ArticleMapping Mapping(string? supplier, InvoiceLine line, string ingredientId, long? factor)
    {
        var m = new ArticleMapping
        {
            SupplierName = supplier,
            Gtin = line.Gtin,
            Observed = line.Name,
            UnitCode = string.IsNullOrEmpty(line.UnitCode) ? null : line.UnitCode,
            IngredientId = ingredientId,
            Factor = factor,
        };
        if (!string.IsNullOrEmpty(supplier)) m.SupplierArticleId = line.SellerArticleId;
        if (string.IsNullOrEmpty(m.SupplierArticleId) && string.IsNullOrEmpty(m.Gtin)) m.Name = line.Name;
        return m;
    }

    // Only a factor read from the article name is kept with the mapping; one from the
    // ingredient's piece weight is looked up at calculation time, so correcting the weight
    // corrects every case.
    static long? Packed(RuleSet rs, Ingredient ing, string unitCode, Pack? pack) =>
        Factors.Of(Scale.Of(rs, ing.Id), ing.Piece, unitCode, pack, null) is (var f, _, FactorSource.Pack) ? f : null;
}

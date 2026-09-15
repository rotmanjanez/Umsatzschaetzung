using Umsatzschätzung.Service;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Suggest;

public sealed record Suggestion(ArticleMapping Mapping, int Confidence, OriginKind Kind);

public sealed class Matcher(ILlmEngine? engine)
{
    public async Task<List<Suggestion>> Suggest(RuleSet rs, string? supplierVatId, InvoiceLine line, CancellationToken ct)
    {
        if (Match.Mapping(rs, supplierVatId, DateOnly.FromDateTime(DateTime.Now), line) is { } hit)
            return [new Suggestion(hit, 100, OriginKind.Exact)];
        if (engine is null) return [];
        var ingredients = ActiveIngredients(rs);
        if (ingredients.Count == 0) return [];
        if (ingredients.Count > Prompt.CategoryThreshold)
        {
            var category = await AskCategory(engine, line, ingredients, ct);
            var narrowed = ingredients.Where(i => i.Category == category).ToList();
            if (narrowed.Count > 0) ingredients = narrowed;
        }
        var ans = await AskIngredient(engine, line, ingredients, ct);
        if (!rs.Ingredients.TryGetValue(ans.IngredientId, out var ing)) return [];
        if (FactorOf(ans.Count, ans.SizeMilli, ing.BaseUnit) is not { } factor) return [];
        var mapping = new ArticleMapping
        {
            SupplierVatId = supplierVatId,
            Gtin = line.Gtin,
            IngredientId = ing.Id,
            Factor = factor,
        };
        if (!string.IsNullOrEmpty(supplierVatId)) mapping.SupplierArticleId = line.SellerArticleId;
        if (string.IsNullOrEmpty(mapping.SupplierArticleId) && string.IsNullOrEmpty(mapping.Gtin)) mapping.Name = line.Name;
        return [new Suggestion(mapping, ans.Confidence, OriginKind.Model)];
    }

    static List<Ingredient> ActiveIngredients(RuleSet rs)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        return rs.Ingredients.Values
            .Where(i => i.Meta.ValidOn(today))
            .OrderBy(i => i.Id, StringComparer.Ordinal)
            .ToList();
    }

    public static long? FactorOf(long count, long sizeMilli, Unit baseUnit)
    {
        if (count <= 0 || sizeMilli <= 0) return null;
        var factor = count * sizeMilli;
        if (baseUnit == Unit.Piece) factor /= 1000;
        return factor <= 0 ? null : factor;
    }

    static async Task<string> AskCategory(ILlmEngine engine, InvoiceLine line, List<Ingredient> ingredients, CancellationToken ct)
    {
        var cats = Prompt.Categories(ingredients);
        if (cats.Count == 0) return "";
        var raw = await engine.Complete(new LlmRequest(
            Prompt.CategoryPrompt,
            Prompt.LineText(line) + "\nWarengruppen:\n" + string.Join("\n", cats) + "\n",
            48), ct);
        return raw.Trim();
    }

    async Task<Answer> AskIngredient(ILlmEngine engine, InvoiceLine line, List<Ingredient> ingredients, CancellationToken ct)
    {
        var raw = await engine.Complete(new LlmRequest(
            Prompt.SystemPrompt,
            Prompt.IngredientUser(line, ingredients),
            96), ct);
        return Prompt.ParseAnswer(raw);
    }
}

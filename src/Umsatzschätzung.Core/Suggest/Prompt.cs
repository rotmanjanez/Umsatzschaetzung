using System.Globalization;
using System.Text;
using System.Text.Json;
using Umsatzschätzung.Model;

namespace Umsatzschätzung.Suggest;

internal sealed record Answer(string IngredientId, long Count, long SizeMilli, int Confidence);

internal static class Prompt
{
    public const int CategoryThreshold = 150;

    public const string SystemPrompt = """
        You map purchase invoice lines of a restaurant to exactly one ingredient from a given list and describe the packaging of one invoice unit. Invoice text and ingredient names are German.
        Rules:
        - ingredientId: exactly one id from the list; "none" if nothing fits (deposit, returnable empties, packaging, services, non-food).
        - count: number of single containers in one invoice unit, taken only from a phrase like "24 x" or "60 Stk" in the description. Without such a phrase count is 1. Alcohol strength (40% vol), article numbers and prices are never a count.
        - size: content of one single container, always in litres when the ingredient's base unit is ml, in kilograms when it is g, and 1 when it is Stück (pieces).
        - Numbers for count and size come only from the description of this line.
        - Goods billed by weight in kg: count 1, size 1.
        - confidence: 0-100, how sure the mapping is.
        Examples (description → count, size):
        "Kiste Pils 20 x 0,5 l" → 20, 0.5
        "Weißwein trocken 0,75 l 12% vol" → 1, 0.75
        "Fass Helles 50 l" → 1, 50
        "Orangensaft 1 l Tetra" → 1, 1
        "Mehl Type 550 25 kg Sack" → 1, 25
        "Rinderhüfte, Abrechnung je kg" → 1, 1
        "Servietten 3-lagig 250 Stk" → 250, 1
        "Pfand Kiste 20 x 0,5 l" → ingredientId "none"
        """;

    public const string CategoryPrompt = """
        Du ordnest Positionen aus Wareneinkaufsrechnungen eines Gastronomiebetriebs einer Warengruppe zu.
        Wähle die Warengruppe, in die die Rechnungsposition am ehesten gehört.
        """;

    const string IngredientGrammarHead = """
        root ::= "{" ws "\"ingredientId\":" ws id "," ws "\"count\":" ws int "," ws "\"size\":" ws num "," ws "\"confidence\":" ws conf ws "}"
        """ + "\nid ::= ";

    const string IngredientGrammarTail = """

        int ::= [1-9] [0-9]? [0-9]? [0-9]? [0-9]?
        num ::= [0-9] [0-9]? [0-9]? [0-9]? [0-9]? ("." [0-9] [0-9]? [0-9]?)?
        conf ::= "100" | [1-9] [0-9]? | "0"
        ws ::= [ \t\n]*
        """ + "\n";

    public static string LineText(InvoiceLine line)
    {
        var b = new StringBuilder();
        b.Append("Rechnungsposition:\nBezeichnung: ").Append(line.Name).Append('\n');
        if (!string.IsNullOrEmpty(line.SellerArticleId)) b.Append("Artikelnummer: ").Append(line.SellerArticleId).Append('\n');
        if (!string.IsNullOrEmpty(line.Gtin)) b.Append("GTIN: ").Append(line.Gtin).Append('\n');
        return b.ToString();
    }

    public static string IngredientUser(InvoiceLine line, IReadOnlyList<Ingredient> ingredients)
    {
        var b = new StringBuilder(LineText(line));
        b.Append("\nZutaten (id | Name | Basiseinheit | Warengruppe):\n");
        foreach (var ing in ingredients)
            b.Append(ing.Id).Append(" | ").Append(ing.Name).Append(" | ").Append(Units.Code(ing.BaseUnit)).Append(" | ").Append(ing.Category).Append('\n');
        return b.ToString();
    }


    public static List<string> Categories(IReadOnlyList<Ingredient> ingredients) =>
        ingredients.Select(i => i.Category).Where(c => c != "").Distinct().Order(StringComparer.Ordinal).ToList();

    static string Literal(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static string JsonString(string s) => Literal("\"" + s + "\"");

    public static string IngredientGrammar(IReadOnlyList<Ingredient> ingredients) =>
        IngredientGrammarHead + string.Join(" | ", ingredients.Select(i => i.Id).Append("none").Select(JsonString)) + IngredientGrammarTail;

    public static string CategoryGrammar(IReadOnlyList<string> cats) => "root ::= " + string.Join(" | ", cats.Select(Literal)) + "\n";

    public static Answer ParseAnswer(string raw)
    {
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var r = doc.RootElement;
            return new Answer(
                r.GetProperty("ingredientId").GetString() ?? "",
                r.GetProperty("count").GetInt64(),
                DecimalMilli(r.GetProperty("size").GetRawText()),
                (int)r.GetProperty("confidence").GetInt64());
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("suggest: Modellantwort unvollständig: " + e.Message, e);
        }
    }

    static long DecimalMilli(string s)
    {
        var dot = s.IndexOf('.');
        var whole = dot < 0 ? s : s[..dot];
        var frac = dot < 0 ? "" : s[(dot + 1)..];
        if (whole == "") whole = "0";
        var w = long.Parse(whole, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var f = long.Parse((frac + "000")[..3], NumberStyles.None, CultureInfo.InvariantCulture);
        return w * 1000 + f;
    }
}

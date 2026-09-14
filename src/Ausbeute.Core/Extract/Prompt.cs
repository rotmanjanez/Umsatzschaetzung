using System.Text.Json;
using System.Text.Json.Serialization;
using Ausbeute.Service;

namespace Ausbeute.Extract;

internal sealed class AnswerLine
{
    [JsonPropertyName("row")] public int Row { get; set; } = -1;
    [JsonPropertyName("quantity")] public string Quantity { get; set; } = "";
    [JsonPropertyName("unit")] public string Unit { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("unitPrice")] public string UnitPrice { get; set; } = "";
    [JsonPropertyName("lineNet")] public string LineNet { get; set; } = "";
    [JsonPropertyName("vat")] public string Vat { get; set; } = "";
}

internal sealed class Answer
{
    [JsonPropertyName("number")] public string Number { get; set; } = "";
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("supplier")] public string Supplier { get; set; } = "";
    [JsonPropertyName("netTotal")] public string NetTotal { get; set; } = "";
    [JsonPropertyName("grossTotal")] public string GrossTotal { get; set; } = "";
    [JsonPropertyName("lines")] public List<AnswerLine> Lines { get; set; } = [];
}

[JsonSerializable(typeof(Answer))]
internal sealed partial class ExtractJsonContext : JsonSerializerContext
{
}

internal static class Prompt
{
    const int BaseTokens = 64;
    const int TokensPerRow = 48;
    const int MaxRowsPerCall = 400;

    public const string SystemPrompt = """
        Du liest deutsche Eingangsrechnungen und Lieferscheine.
        Der Text ist eine Seite, Zeile für Zeile; vor dem senkrechten Strich steht die Zeilennummer, die Leerzeichen bilden die Spalten des Dokuments nach.
        Aufgabe: Extrahiere die Kopfdaten und alle Positionen.
        Kopfdaten: number = Rechnungsnummer (nur bei einem Lieferschein ohne Rechnungsnummer die Lieferscheinnummer; die Nummer selbst, ohne Beschriftung), date = Rechnungs- oder Lieferdatum, supplier = Name der ausstellenden Firma (nicht der Empfänger), netTotal = Nettosumme, grossTotal = Bruttosumme (Endbetrag).
        Regeln:
        - Jede Position genau so, wie sie gedruckt ist: eine Position je gedruckter Zeile, nichts zusammenrechnen, nichts erfinden, nichts weglassen. Auch Pfand, Leergut, Zuschläge und Rabattzeilen sind Positionen. Spaltenköpfe, Zwischensummen, Steuerzeilen und Endsummen sind keine Positionen.
        - Zahlen exakt wie gedruckt übernehmen, deutsches Format wie 1.234,56 ist erlaubt. quantity = Menge, unit = Einheit wie gedruckt (kg, Stk, Kiste, Flasche …; leer, wenn keine Einheit gedruckt ist, nie der Artikeltext), unitPrice = Einzelpreis, lineNet = Gesamtpreis der Zeile, vat = Steuersatz in Prozent (z. B. 7 oder 19; leer, wenn kein Steuersatz gedruckt ist oder die Rechnung umsatzsteuerbefreit ist).
        - Steht der Steuersatz nicht in der Zeile, sondern nur im Summenblock, und gilt er für alle Positionen, trage ihn bei jeder Position ein.
        - row = Nummer der Zeile, in der Menge und Preis der Position stehen. Steht die Artikelbezeichnung in einer eigenen Zeile, gehört sie zur Position darunter oder darüber, die Menge und Preis trägt.
        - name ist der gedruckte Artikeltext der Zeile, nie der Spaltenkopf. Unbekannte oder nicht gedruckte Werte bleiben leer ("").
        Beispiel: aus der Zeile
        14|   6  Kiste        Mineralwasser 12 x 0,7 l        9,60        57,60
        wird bei einem Summenblock mit "MwSt 19 %" die Position {"row": 14, "quantity": "6", "unit": "Kiste", "name": "Mineralwasser 12 x 0,7 l", "unitPrice": "9,60", "lineNet": "57,60", "vat": "19"}.
        """;

    public const string Grammar = """
        root ::= "{" ws "\"number\":" ws str "," ws "\"date\":" ws str "," ws "\"supplier\":" ws str "," ws "\"netTotal\":" ws str "," ws "\"grossTotal\":" ws str "," ws "\"lines\":" ws "[" ws (line (ws "," ws line)*)? ws "]" ws "}"
        line ::= "{" ws "\"row\":" ws int "," ws "\"quantity\":" ws str "," ws "\"unit\":" ws str "," ws "\"name\":" ws str "," ws "\"unitPrice\":" ws str "," ws "\"lineNet\":" ws str "," ws "\"vat\":" ws str ws "}"
        str ::= "\"" char* "\""
        char ::= [^"\\\x00-\x1F\x7F] | "\\" ["\\/bfnrt]
        int ::= "-1" | [0-9] [0-9]? [0-9]? [0-9]?
        ws ::= [ \t\n]*
        """ + "\n";

    public static async Task<Answer> Ask(ILlmEngine engine, IReadOnlyList<Row> rows, CancellationToken ct)
    {
        var request = new LlmRequest(SystemPrompt, Rows.Render(rows), Grammar, BaseTokens + TokensPerRow * Math.Min(rows.Count, MaxRowsPerCall));
        var raw = await engine.Complete(request, ct);
        try
        {
            return JsonSerializer.Deserialize(raw.Trim(), ExtractJsonContext.Default.Answer)
                   ?? throw new InvalidDataException("extract: Modellantwort lesen: leer");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("extract: Modellantwort lesen: " + e.Message, e);
        }
    }
}

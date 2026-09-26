using System.Text.Json.Serialization;

namespace Umsatzschaetzung.Model;

public sealed class InventoryEntry
{
    public string IngredientId { get; set; } = "";
    public long Opening { get; set; }
    public long Closing { get; set; }
    public string Unit { get; set; } = "";
}

public sealed class YieldChoice
{
    public string? IngredientId { get; set; }
    public string? CategoryId { get; set; }
    // Ohne Regel wird nichts abgezogen.
    public string? YieldRuleId { get; set; }
}

public sealed class PinnedPortions
{
    public string ProductId { get; set; } = "";
    public long Portions { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class DeclaredRevenue
{
    public long Vat { get; set; }
    public long Net { get; set; }
}

public sealed class Taxpayer
{
    public string Name { get; set; } = "";
    public string TaxNumber { get; set; } = "";
    public string PabNumber { get; set; } = "";
    public string Gewerbe { get; set; } = "";
}

public sealed class CaseProduct
{
    public string ProductId { get; set; } = "";
    public long GrossPrice { get; set; }
    public long Vat { get; set; }
    // Rezeptur nur dieser Prüfung; null heißt: die des Katalogs. Basis ist die Prüfsumme
    // der Katalogrezeptur, von der die Kopie stammt.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RecipeLine>? Recipe { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long RecipeBasis { get; set; }
}

public sealed class Case
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }
    public Taxpayer Taxpayer { get; set; } = new();
    public List<DeclaredRevenue> Declared { get; set; } = [];
    public List<InventoryEntry> Inventory { get; set; } = [];
    public List<Invoice> Invoices { get; set; } = [];
    public List<CaseProduct> Products { get; set; } = [];
    public List<YieldChoice> Yields { get; set; } = [];
    public List<PinnedPortions> Pinned { get; set; } = [];
    // Zutaten, die in diesem Betrieb keinen Umsatz bringen, etwa Reinigungsmittel.
    public List<string> NoRevenue { get; set; } = [];
    // Was das Programm beim Einlesen selbst zugeordnet hat. Es bleibt bei der Prüfung, bis eine
    // Person es bestätigt und es damit in die gemeinsamen Regeln kommt.
    public Dictionary<string, ArticleMapping> Mappings { get; set; } = [];
    // Leer heißt: die Standardvorlage der Regeln.
    public string? TemplateId { get; set; }
    // Der Zähler gilt nur in der Regel-Datenbank, die MappedStore nennt.
    public string? MappedStore { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long MappedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    public bool MappedTo(RuleSet rs) => MappedStore == rs.Store && MappedAt == rs.Version;
}

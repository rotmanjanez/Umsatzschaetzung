using System.Text.Json.Serialization;

namespace Umsatzschätzung.Model;

public sealed class InventoryEntry
{
    public string IngredientId { get; set; } = "";
    public long Opening { get; set; }
    public long Closing { get; set; }
}

public sealed class YieldChoice
{
    public string? IngredientId { get; set; }
    public string? Category { get; set; }
    public string YieldRuleId { get; set; } = "";
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
}

public sealed class CaseProduct
{
    public string ProductId { get; set; } = "";
    public long GrossPrice { get; set; }
    public long Vat { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Disabled { get; set; }
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
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

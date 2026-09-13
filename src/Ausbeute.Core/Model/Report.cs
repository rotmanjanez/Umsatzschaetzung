using System.Text.Json.Serialization;

namespace Ausbeute.Model;

public enum SourceKind
{
    [JsonStringEnumMemberName("invoiceLine")] InvoiceLine,
    [JsonStringEnumMemberName("rule")] Rule,
    [JsonStringEnumMemberName("pinned")] Pinned,
    [JsonStringEnumMemberName("allocation")] Allocation,
    [JsonStringEnumMemberName("inventory")] Inventory,
    [JsonStringEnumMemberName("case")] Case,
}

public sealed class SourceRef
{
    public SourceKind Kind { get; set; }
    public string? InvoiceId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long LineNo { get; set; }
    public Entity? Entity { get; set; }
    public string? EntityId { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ChangeId { get; set; }
    public string? Reason { get; set; }
}

public sealed class Node
{
    public string Label { get; set; } = "";
    public long Value { get; set; }
    public ValueUnit Unit { get; set; }
    public string? Formula { get; set; }
    public List<Node> Inputs { get; set; } = [];
    public List<SourceRef> Sources { get; set; } = [];
}

public sealed class Totals
{
    public long CalculatedRevenueNet { get; set; }
    public long Purchases { get; set; }
    public long CostOfGoods { get; set; }
    public long StockChange { get; set; }
    public long GrossProfit { get; set; }
    public long Markup { get; set; }
    public long Portions { get; set; }
    public long UnmappedCost { get; set; }
    public long UnusedCost { get; set; }
    public long ExcludedShare { get; set; }
}

public sealed class UnmappedLine
{
    public string InvoiceId { get; set; } = "";
    public long LineNo { get; set; }
    public string Name { get; set; } = "";
    public long LineNet { get; set; }
}

public sealed class UnusedLine
{
    public string InvoiceId { get; set; } = "";
    public long LineNo { get; set; }
    public string Name { get; set; } = "";
    public long LineNet { get; set; }
    public string IngredientId { get; set; } = "";
}

public sealed class ProductPortions
{
    public string ProductId { get; set; } = "";
    public long Portions { get; set; }
    public bool Pinned { get; set; }
}

public sealed class Leftover
{
    public string IngredientId { get; set; } = "";
    public long Qty { get; set; }
}

public sealed class IngredientRow
{
    public string IngredientId { get; set; } = "";
    public long Bought { get; set; }
    public long Cost { get; set; }
    public long Used { get; set; }
    public long UsedCost { get; set; }
    public long Sellable { get; set; }
    public long Leftover { get; set; }
}

public sealed class Allocation
{
    public int Component { get; set; }
    public List<ProductPortions> Products { get; set; } = [];
    public List<string> Binding { get; set; } = [];
    public List<Leftover> Leftover { get; set; } = [];
    public long Grid { get; set; }
    public long States { get; set; }
    public bool Approximate { get; set; }
}

public sealed class ProductRow
{
    public string ProductId { get; set; } = "";
    public long Portions { get; set; }
    public bool Pinned { get; set; }
    public long GrossPrice { get; set; }
    public long Vat { get; set; }
    public long RevenueNet { get; set; }
    public bool Disabled { get; set; }
    public bool PriceMissing { get; set; }
}

public sealed class Report
{
    public string CaseId { get; set; } = "";
    public DateTimeOffset ComputedAt { get; set; }
    public Totals Totals { get; set; } = new();
    public Node Root { get; set; } = new();
    public List<IngredientRow> Ingredients { get; set; } = [];
    public List<ProductRow> Products { get; set; } = [];
    public List<UnmappedLine> Unmapped { get; set; } = [];
    public List<UnusedLine> Unused { get; set; } = [];
    public List<Allocation> Allocations { get; set; } = [];
    public List<Flag> Warnings { get; set; } = [];
}

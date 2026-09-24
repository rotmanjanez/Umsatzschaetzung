using System.Collections.ObjectModel;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public enum Exclusion { Unmapped, NoFactor, Unused, NoRevenue }

public sealed class ExcludedRow(LineGroup group, Exclusion why, string? ingredientId, string ingredient)
{
    public LineGroup Group { get; } = group;
    public Exclusion Why { get; } = why;
    public string? IngredientId { get; } = ingredientId;
    public string Name => Group.Name;
    public string Ingredient { get; } = ingredient;
    public long Net { get; set; }
    public string LineNet => Format.Cents(Net);
    public bool NoRevenue => Why == Exclusion.NoRevenue;
}

public sealed class ExclusionModel : Observable
{
    string omittedSummary = "", unusedSummary = "", deposit = "", why = "", whyTitle = "";
    bool hasOmitted, hasUnused;
    ExcludedRow? current;

    public ObservableCollection<ExcludedRow> Omitted { get; } = [];
    public ObservableCollection<ExcludedRow> Unused { get; } = [];
    public string OmittedSummary { get => omittedSummary; set => Set(ref omittedSummary, value); }
    public string UnusedSummary { get => unusedSummary; set => Set(ref unusedSummary, value); }
    public bool HasOmitted { get => hasOmitted; set { if (Set(ref hasOmitted, value)) RaiseExcluded(); } }
    public bool HasUnused { get => hasUnused; set { if (Set(ref hasUnused, value)) RaiseExcluded(); } }
    public bool HasExcluded => hasOmitted || hasUnused;
    public bool NoneExcluded => !HasExcluded;
    public string Deposit { get => deposit; set { if (Set(ref deposit, value)) Raise(nameof(HasDeposit)); } }
    public bool HasDeposit => deposit != "";
    public string Why { get => why; set { if (Set(ref why, value)) Raise(nameof(HasWhy)); } }
    public string WhyTitle { get => whyTitle; set => Set(ref whyTitle, value); }
    public bool HasWhy => why != "";
    public ExcludedRow? Current { get => current; private set { if (Set(ref current, value)) { Raise(nameof(CanDropRevenue)); Raise(nameof(CanRestoreRevenue)); } } }
    public bool CanDropRevenue => current?.Why == Exclusion.Unused;
    public bool CanRestoreRevenue => current?.Why == Exclusion.NoRevenue;

    void RaiseExcluded()
    {
        Raise(nameof(HasExcluded));
        Raise(nameof(NoneExcluded));
    }

    // One row per mapping group, as on the Zuordnung tab, so a row can be reassigned in place.
    public void Fill(Report r, Case c, RuleSet rs)
    {
        var s = r.Totals;
        var omitted = s.UnmappedCost + s.NoRevenueCost;
        OmittedSummary = omitted == 0 ? "keine" : Format.Cents(omitted) + " (" + Format.Bp(s.ExcludedShare) + " der Einkäufe)";
        var estimated = r.Estimated.Where(e => e.Source == EstimateSource.Unused).Sum(e => e.RevenueNet);
        var share = s.Purchases == 0 ? 0 : s.UnusedCost * Bp.Full / s.Purchases;
        UnusedSummary = s.UnusedCost == 0 ? "keine"
            : Format.Cents(s.UnusedCost) + " (" + Format.Bp(share) + " der Einkäufe), geschätzter Umsatz " + Format.Cents(estimated);
        Deposit = r.Deposits.Count == 0 ? ""
            : $"Nicht in den Einkäufen: Pfand berechnet +{Format.Cents(s.DepositCharged)}, Leergut gutgeschrieben {Format.Cents(s.DepositRefunded)}";
        var at = new Dictionary<(string, long), (LineGroup Group, Invoice Invoice, InvoiceLine Line)>();
        foreach (var g in LineGroup.Of(c, rs))
            foreach (var (i, j) in g.Lines)
                at[(c.Invoices[i].Id, c.Invoices[i].Lines[j].No)] = (g, c.Invoices[i], c.Invoices[i].Lines[j]);
        var rows = new Dictionary<LineGroup, ExcludedRow>();
        void Add(string invoiceId, long no, long net, Func<LineGroup, Invoice, InvoiceLine, ExcludedRow> row)
        {
            if (!at.TryGetValue((invoiceId, no), out var hit)) return;
            if (!rows.TryGetValue(hit.Group, out var existing)) rows[hit.Group] = existing = row(hit.Group, hit.Invoice, hit.Line);
            existing.Net += net;
        }
        foreach (var l in r.Unmapped)
            Add(l.InvoiceId, l.LineNo, l.LineNet, (g, inv, line) => Match.Mapping(rs, inv.SupplierName, inv.Date, line) is { } m
                ? new ExcludedRow(g, Exclusion.NoFactor, m.IngredientId, Names.Ingredient(rs, m.IngredientId))
                : new ExcludedRow(g, Exclusion.Unmapped, null, ""));
        foreach (var l in r.NoRevenue)
            Add(l.InvoiceId, l.LineNo, l.LineNet, (g, _, _) => new ExcludedRow(g, Exclusion.NoRevenue, null, ""));
        foreach (var l in r.Unused)
            Add(l.InvoiceId, l.LineNo, l.LineNet, (g, _, _) => new ExcludedRow(g, Exclusion.Unused, l.IngredientId, Names.Ingredient(rs, l.IngredientId)));
        Omitted.Clear();
        Unused.Clear();
        foreach (var row in rows.Values.OrderByDescending(x => x.Net)) (row.Why == Exclusion.Unused ? Unused : Omitted).Add(row);
        HasOmitted = Omitted.Count > 0;
        HasUnused = Unused.Count > 0;
    }

    public void Explain(ExcludedRow? row)
    {
        Current = row;
        WhyTitle = row?.Why == Exclusion.Unused ? "NICHT TEIL DER RGAS-ERMITTLUNG" : "NICHT IN DER UMSATZSCHÄTZUNG";
        Why = row?.Why switch
        {
            null => "",
            Exclusion.Unused => $"„{row.Ingredient}“ steht in keiner Rezeptur des Sortiments; der Umsatz wird über den Aufschlagsatz geschätzt",
            Exclusion.NoRevenue => "In dieser Prüfung als ohne Umsatz festgelegt",
            Exclusion.NoFactor => $"Der Zuordnung zu „{row.Ingredient}“ fehlt der Faktor",
            _ => "Keiner Zutat zugeordnet",
        };
    }
}

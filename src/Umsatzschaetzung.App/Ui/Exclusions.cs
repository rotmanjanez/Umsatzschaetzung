using System.Collections.ObjectModel;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.App.Ui;

public enum Exclusion { Unmapped, Unused, NoRevenue }

public sealed class ExcludedRow(LineGroup group, Exclusion why, string? ingredientId, string ingredient)
{
    public LineGroup Group { get; } = group;
    public Exclusion Why { get; } = why;
    public string? IngredientId { get; } = ingredientId;
    public string Name => Group.Name;
    public string Ingredient { get; } = ingredient;
    public long Net { get; set; }
    public string LineNet => Format.Cents(Net);
    public bool CanDropRevenue => Why == Exclusion.Unused;
    public bool CanRestoreRevenue => Why == Exclusion.NoRevenue;
    public string DropTip => $"„{Ingredient}“ bringt in diesem Betrieb keinen Umsatz";
    public string RestoreTip => $"„{Ingredient}“ bringt doch Umsatz";
}

public sealed class ExclusionModel : Observable
{
    string omittedSummary = "", unusedSummary = "", deposit = "";
    bool hasOmitted, hasUnused;

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
        var at = new Dictionary<(string, long), LineGroup>();
        foreach (var g in LineGroup.Of(c, rs))
            foreach (var (i, j) in g.Lines)
                at[(c.Invoices[i].Id, c.Invoices[i].Lines[j].No)] = g;
        var rows = new Dictionary<LineGroup, ExcludedRow>();
        void Add(string invoiceId, long no, long net, Func<LineGroup, ExcludedRow> row)
        {
            if (!at.TryGetValue((invoiceId, no), out var g)) return;
            if (!rows.TryGetValue(g, out var existing)) rows[g] = existing = row(g);
            existing.Net += net;
        }
        foreach (var l in r.Unmapped)
            Add(l.InvoiceId, l.LineNo, l.LineNet, g => new ExcludedRow(g, Exclusion.Unmapped, null, ""));
        foreach (var l in r.NoRevenue)
            Add(l.InvoiceId, l.LineNo, l.LineNet, g => new ExcludedRow(g, Exclusion.NoRevenue, l.IngredientId, Names.Ingredient(rs, l.IngredientId)));
        foreach (var l in r.Unused)
            Add(l.InvoiceId, l.LineNo, l.LineNet, g => new ExcludedRow(g, Exclusion.Unused, l.IngredientId, Names.Ingredient(rs, l.IngredientId)));
        Omitted.Clear();
        Unused.Clear();
        foreach (var row in rows.Values.OrderByDescending(x => x.Net)) (row.Why == Exclusion.Unused ? Unused : Omitted).Add(row);
        HasOmitted = Omitted.Count > 0;
        HasUnused = Unused.Count > 0;
    }
}

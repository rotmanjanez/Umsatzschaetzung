using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;

namespace Umsatzschätzung.App.Ui;

public sealed class LineGroup
{
    public required string Supplier { get; init; }
    public required string? VatId { get; init; }
    public required string? Article { get; init; }
    public required string Name { get; init; }
    public required string Unit { get; init; }
    public List<(int Invoice, int Line)> Lines { get; } = [];
    public int Count => Lines.Count;
}

public sealed class CandidateRow(MappingCandidate candidate) : Observable
{
    bool selected;

    public MappingCandidate Candidate { get; } = candidate;
    public string Display => Candidate.Display;
    public bool IsExact => Candidate.Kind == OriginKind.Exact;
    public bool IsLexical => Candidate.Kind == OriginKind.Lexical;
    public bool IsManual => Candidate.Kind == OriginKind.Manual;
    public string Confidence => IsLexical ? "Sicherheit " + Candidate.Confidence + " %" : "";
    public bool Selected { get => selected; set => Set(ref selected, value); }
}

public sealed class MappingModel : Observable
{
    bool empty = true, noInvoices, hasSelection, loading, manual;
    string title = "", supplier = "", article = "", unit = "", count = "", factor = "";
    Ingredient? ingredient;
    List<Ingredient> ingredients = [];

    public ObservableCollection<LineGroup> Groups { get; } = [];
    public ObservableCollection<CandidateRow> Candidates { get; } = [];
    public bool Empty { get => empty; set { if (Set(ref empty, value)) Raise(nameof(AllMapped)); } }
    public bool NoInvoices { get => noInvoices; set { if (Set(ref noInvoices, value)) Raise(nameof(AllMapped)); } }
    public bool AllMapped => empty && !noInvoices;
    public bool HasSelection { get => hasSelection; set { if (Set(ref hasSelection, value)) Raise(nameof(NoSelection)); } }
    public bool NoSelection => !hasSelection;
    public bool Loading { get => loading; set { if (Set(ref loading, value)) Raise(nameof(NoCandidates)); } }
    public bool NoCandidates => !loading && Candidates.Count == 0;
    public bool ShowCandidates => !manual;
    public bool Manual
    {
        get => manual;
        set
        {
            if (!Set(ref manual, value)) return;
            Raise(nameof(ShowCandidates));
            Raise(nameof(ManualLabel));
            Raise(nameof(CanAssign));
        }
    }
    public string ManualLabel => manual ? "Vorschlag verwenden" : "Manuell zuordnen";
    public bool CanAssign => manual || Candidates.Any(c => c.Selected);
    public string Title { get => title; set => Set(ref title, value); }
    public string Supplier { get => supplier; set => Set(ref supplier, value); }
    public string Article { get => article; set => Set(ref article, value); }
    public string Unit { get => unit; set => Set(ref unit, value); }
    public string Count { get => count; set => Set(ref count, value); }
    public string Factor { get => factor; set => Set(ref factor, value); }
    public Ingredient? Ingredient { get => ingredient; set => Set(ref ingredient, value); }
    public List<Ingredient> Ingredients { get => ingredients; set => Set(ref ingredients, value); }

    public void SetCandidates(List<MappingCandidate> candidates)
    {
        foreach (var c in Candidates) c.Changed -= CandidateChanged;
        Candidates.Clear();
        foreach (var c in candidates)
        {
            var row = new CandidateRow(c);
            row.Changed += CandidateChanged;
            Candidates.Add(row);
        }
        if (Candidates.Count > 0) Candidates[0].Selected = true;
        Manual = Candidates.Count == 0;
        Raise(nameof(NoCandidates));
        Raise(nameof(CanAssign));
    }

    void CandidateChanged() => Raise(nameof(CanAssign));
}

public partial class MappingView : Screen
{
    readonly MappingModel model = new();
    int suggestSeq;

    public MappingView(Session session) : base(session)
    {
        InitializeComponent();
        DataContext = model;
        Search.Width = double.NaN;
        Search.Attach(model.Groups, g => g.Supplier + " " + g.Name + " " + g.Article);
        Groups.ItemsSource = Search.View;
    }

    protected override void OnEnter()
    {
        IngredientBox.SetCategoryNames(this, Session.CategoryNames);
        model.Ingredients = Session.Ingredients();
        model.Groups.Clear();
        foreach (var g in UnmappedGroups()) model.Groups.Add(g);
        model.NoInvoices = Session.Case?.Invoices.Count == 0;
        model.Empty = model.Groups.Count == 0;
        model.SetCandidates([]);
        model.HasSelection = false;
    }

    List<LineGroup> UnmappedGroups()
    {
        var groups = new Dictionary<string, LineGroup>();
        if (Session.Case is null) return [];
        for (var i = 0; i < Session.Case.Invoices.Count; i++)
        {
            var inv = Session.Case.Invoices[i];
            for (var j = 0; j < inv.Lines.Count; j++)
            {
                var l = inv.Lines[j];
                if (!string.IsNullOrEmpty(l.MappingId)) continue;
                var key = inv.SupplierVatId + "|" + inv.SupplierName + "|" + (string.IsNullOrEmpty(l.SellerArticleId) ? l.Name : l.SellerArticleId);
                if (!groups.TryGetValue(key, out var g))
                {
                    g = new LineGroup { Supplier = inv.SupplierName, VatId = inv.SupplierVatId, Article = l.SellerArticleId, Name = l.Name, Unit = l.UnitCode };
                    groups[key] = g;
                }
                g.Lines.Add((i, j));
            }
        }
        return groups.Values.OrderBy(g => g.Supplier + g.Name, StringComparer.Ordinal).ToList();
    }

    async void GroupSelected(object? sender, SelectionChangedEventArgs e)
    {
        var seq = ++suggestSeq;
        model.SetCandidates([]);
        if (Groups.SelectedItem is not LineGroup g || Session.Case is null)
        {
            model.HasSelection = false;
            return;
        }
        model.Title = g.Name;
        model.Supplier = g.Supplier;
        model.Article = g.Article ?? "";
        model.Unit = g.Unit;
        model.Count = g.Count.ToString();
        model.HasSelection = true;
        model.Loading = true;
        var (inv, line) = g.Lines[0];
        var lineItem = Session.Case.Invoices[inv].Lines[line];
        await Session.Run(async () =>
        {
            var resp = await Session.Service.SuggestMapping(lineItem, g.VatId, Ct);
            if (seq != suggestSeq) return;
            model.Loading = false;
            model.SetCandidates(resp.Candidates);
        });
        if (seq == suggestSeq) model.Loading = false;
    }

    void ToggleManual(object? sender, RoutedEventArgs e) => model.Manual = !model.Manual;

    async void Assign(object? sender, RoutedEventArgs e)
    {
        if (Groups.SelectedItem is not LineGroup g || Session.Case is null) return;
        if (!model.Manual)
        {
            var chosen = model.Candidates.FirstOrDefault(c => c.Selected);
            if (chosen is not null) await AssignId(g, chosen.Candidate.Mapping.Id);
            return;
        }
        var factor = Input.Int(model.Factor);
        if (model.Ingredient is null || factor is null)
        {
            Session.Fail("Bitte Zutat und Faktor angeben.");
            return;
        }
        var mapping = new ArticleMapping
        {
            Id = Session.NewId("map"),
            SupplierVatId = g.VatId,
            SupplierArticleId = g.Article,
            Name = g.Name,
            Observed = g.Name,
            UnitCode = g.Unit,
            IngredientId = model.Ingredient.Id,
            Factor = factor.Value,
            Confirmed = true,
        };
        if (await Session.Put(mapping, Ct))
            await AssignId(g, mapping.Id);
    }

    void GoInvoices(object? sender, RoutedEventArgs e) => Session.Go(Tab.Invoices);

    void GoCalc(object? sender, RoutedEventArgs e) => Session.Go(Tab.Calc);

    async Task AssignId(LineGroup g, string mappingId)
    {
        if (Session.Case is null) return;
        foreach (var (inv, line) in g.Lines) Session.Case.Invoices[inv].Lines[line].MappingId = mappingId;
        if (await Session.SaveCase(Ct))
            OnEnter();
    }
}

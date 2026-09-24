using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Suggest;

namespace Umsatzschaetzung.App.Ui;

public sealed class CandidateRow(MappingCandidate candidate, string label, bool current, string usedIn = "") : Observable
{
    bool selected;

    public MappingCandidate Candidate { get; } = candidate;
    public string Display { get; } = label;
    public bool IsExact => Candidate.Kind == OriginKind.Exact;
    public bool IsSuggested => Candidate.Kind == OriginKind.Encoder;
    public string ExactText => current ? "Aktuelle Zuordnung" : "Exakter Treffer";
    public string Confidence => IsSuggested ? "Sicherheit " + Candidate.Confidence + " %" : "";
    public bool InRecipe => UsedIn != "";
    public string UsedIn { get; } = usedIn;
    public bool Selected { get => selected; set => Set(ref selected, value); }
}

public sealed class SnippetRow(Invoice invoice, int line) : Observable
{
    bool loading = true;

    public Invoice Invoice { get; } = invoice;
    public int Line { get; } = line;
    public string Number => Invoice.Number;
    public string Date { get; } = Format.Date(invoice.Date);
    public Bitmap? Scan { get; private set; }
    public string Excerpt { get; private set; } = "";
    public bool Loading => loading;
    public bool HasScan => Scan is not null;
    public bool HasExcerpt => Excerpt != "";
    public bool Missing => !loading && !HasScan && !HasExcerpt;

    public void Show(Bitmap? image, string? text)
    {
        Scan = image;
        Excerpt = text ?? "";
        loading = false;
        foreach (var name in (string[])[nameof(Scan), nameof(Excerpt), nameof(Loading), nameof(HasScan), nameof(HasExcerpt), nameof(Missing)])
            Raise(name);
    }
}

public sealed class MappingDetailModel : Observable
{
    bool hasSelection, loading, mapping, manual, currentAuto;
    string title = "", supplier = "", article = "", total = "", factor = "", current = "";
    string assigned = "", prefilled = "";
    Ingredient? ingredient;
    List<Ingredient> ingredients = [];
    RuleSet? rules;
    InvoiceLine? line;
    long quantity;
    string? target;
    Unit? unit;
    Pack? pack;
    long? stored;
    (long Factor, long Per, FactorSource Source)? resolved;

    public ObservableCollection<CandidateRow> Candidates { get; } = [];
    public ObservableCollection<SnippetRow> Snippets { get; } = [];
    public bool HasSnippets => Snippets.Count > 0;
    public string SnippetsTitle => Snippets.Count == 1 ? "BELEG" : Snippets.Count + " BELEGE";
    public bool HasSelection { get => hasSelection; set { if (Set(ref hasSelection, value)) Raise(nameof(NoSelection)); } }
    public bool NoSelection => !hasSelection;
    public bool Loading { get => loading; set { if (Set(ref loading, value)) Raise(nameof(NoCandidates)); } }
    public bool NoCandidates => !loading && Candidates.Count == 0;
    public bool Mapping { get => mapping; set { if (Set(ref mapping, value)) Raise(nameof(CanAssign)); } }
    public bool ShowCandidates => !manual;
    public bool Manual
    {
        get => manual;
        set
        {
            if (!Set(ref manual, value)) return;
            Raise(nameof(ShowCandidates));
            Raise(nameof(ShowFields));
            Raise(nameof(ManualLabel));
            Raise(nameof(AssignLabel));
            Raise(nameof(CanAssign));
            Prefill();
        }
    }
    public bool ShowFactor => unit is not null && resolved is not { Source: FactorSource.Table };
    public bool NeedsFactor => ShowFactor && resolved is null && stored is null;
    public bool ShowFields => manual || ShowFactor;
    public string FactorLabel => NeedsFactor ? "Faktor *" : "Faktor";
    public string ManualLabel => manual ? "Vorschlag verwenden" : "Manuell zuordnen";
    public string AssignLabel => !manual && currentAuto && Candidates.FirstOrDefault(c => c.Selected) is { IsExact: true } ? "Bestätigen" : "Zuordnen";
    public bool CanAssign => !mapping && (manual || Candidates.Any(c => c.Selected));
    public string Title { get => title; set => Set(ref title, value); }
    public string Supplier { get => supplier; set => Set(ref supplier, value); }
    public string Article { get => article; set { if (Set(ref article, value)) Raise(nameof(HasArticle)); } }
    public bool HasArticle => article != "";
    public string Total { get => total; set => Set(ref total, value); }
    public string Current { get => current; set { if (Set(ref current, value)) Raise(nameof(HasCurrent)); } }
    public bool HasCurrent => current != "";
    public bool CurrentAuto { get => currentAuto; set { if (Set(ref currentAuto, value)) Raise(nameof(AssignLabel)); } }
    public string Assigned { get => assigned; set { if (Set(ref assigned, value)) Raise(nameof(HasAssigned)); } }
    public bool HasAssigned => assigned != "";
    public string Factor
    {
        get => factor;
        set
        {
            if (!Set(ref factor, value)) return;
            Raise(nameof(FactorHint));
            Raise(nameof(FactorOrigin));
            Raise(nameof(HasOrigin));
            Raise(nameof(Estimate));
        }
    }
    public string FactorUnit => unit is { } u ? Format.UnitName(u) + " / " + Units.Label(line!.UnitCode) : "";

    // What the typed factor makes of this position: its content per package, then the whole delivery.
    public string FactorHint
    {
        get
        {
            if (unit is not { } u) return "";
            var billed = Units.Label(line!.UnitCode);
            var name = Names.Ingredient(rules!, target!);
            if (Typed is not { } f)
                return $"Wie viel {Format.UnitName(u)} {name} enthält 1 {billed}?";
            var each = Format.Qty(f, u);
            return $"1 {billed} = {each} {name} · {Format.Milli(quantity)} {billed} × {each} = {Format.Qty(quantity * f / 1000, u)}";
        }
    }

    // Where an untouched factor comes from, or where a typed one goes. The piece weight is only a
    // guess: a cash&carry "Stk" can be a 10 kg sack, which the price per kilogram gives away.
    public string FactorOrigin
    {
        get
        {
            if (!ShowFactor) return "";
            var price = PricePer();
            if (Teaches)
                return $"Wird als Stückgewicht für {Names.Ingredient(rules!, target!)} gespeichert" + price;
            if (Untouched && resolved is { Source: FactorSource.Pack } && pack is { } p)
                return "Aus der Packungsangabe: " + PackText(p) + price;
            if (Untouched && stored is null && resolved is { Source: FactorSource.Piece } && Weight is { } w)
                return $"Richtwert der Zutat: 1 Stück ≈ {Format.Qty(w.Amount, w.Unit)}{price} — stimmt das für diesen Artikel?";
            return "";
        }
    }
    public bool HasOrigin => FactorOrigin != "";
    public bool Estimate => Untouched && stored is null && resolved is { Source: FactorSource.Piece };

    Piece? Weight => rules?.Ingredients.GetValueOrDefault(target ?? "")?.Piece;
    long? Typed => Input.Int(factor) is > 0 and var f ? f : null;
    bool Untouched => factor == prefilled || factor.Trim() == "";

    // A single piece measured in g or ml says how heavy the ingredient is, not this article:
    // that belongs to the ingredient, where every other line of it finds it.
    bool Learns => unit is Unit.G or Unit.Ml && pack is null
        && Units.Lookup(line!.UnitCode) is { Container: false, Base: Unit.Piece };
    bool Teaches => Learns && !Untouched && Typed is { } t && t != resolved?.Factor;

    string PricePer()
    {
        if (unit is not (Unit.G or Unit.Ml) || Typed is not { } f || line is not { UnitPrice: > 0 } l) return "";
        var each = l.PriceBaseQty > 0 ? l.UnitPrice * 1000 / l.PriceBaseQty : l.UnitPrice;
        return " · ≈ " + Format.Cents((each * 1000 / f + 5000) / 10000) + (unit == Unit.G ? "/kg" : "/l");
    }

    string PackText(Pack p)
    {
        if (p.Base == Unit.Piece) return p.Count + " Stück";
        var size = Format.Qty(p.Size, p.Base ?? unit!.Value);
        return p.Count > 1 ? p.Count + " × " + size : size;
    }

    // Nothing to store when the factor is what the line resolves anyway; a piece weight is kept
    // with the ingredient, a pack size with the mapping as the matcher does it.
    public bool Decide(out long? mappingFactor, out Piece? piece)
    {
        mappingFactor = null;
        piece = null;
        if (!ShowFactor) return true;
        if (Untouched)
        {
            mappingFactor = stored ?? (resolved is (var r, 1, FactorSource.Pack) ? r : null);
            return !NeedsFactor;
        }
        if (Typed is not { } f) return false;
        if (resolved is (var same, 1, var source) && f == same) mappingFactor = source == FactorSource.Pack ? f : null;
        else if (Learns) piece = new Piece(f, unit!.Value);
        else mappingFactor = f;
        return true;
    }

    public Ingredient? Ingredient { get => ingredient; set { if (Set(ref ingredient, value)) Prefill(); } }
    public List<Ingredient> Ingredients { get => ingredients; set => Set(ref ingredients, value); }

    public void Select(RuleSet? rs, InvoiceLine? l, long qty = 0)
    {
        rules = rs;
        line = l;
        quantity = qty;
        Prefill();
    }

    public void ClearSnippets()
    {
        Snippets.Clear();
        Raise(nameof(HasSnippets));
        Raise(nameof(SnippetsTitle));
    }

    public void SetSnippets(IEnumerable<SnippetRow> rows)
    {
        ClearSnippets();
        foreach (var row in rows) Snippets.Add(row);
        Raise(nameof(HasSnippets));
        Raise(nameof(SnippetsTitle));
    }

    public void SetCandidates(List<CandidateRow> candidates)
    {
        foreach (var c in Candidates) c.Changed -= CandidateChanged;
        Candidates.Clear();
        foreach (var row in candidates)
        {
            row.Changed += CandidateChanged;
            Candidates.Add(row);
        }
        if (Candidates.Count > 0) Candidates[0].Selected = true;
        Manual = Candidates.Count == 0;
        Raise(nameof(NoCandidates));
        CandidateChanged();
    }

    void CandidateChanged()
    {
        Raise(nameof(CanAssign));
        Raise(nameof(AssignLabel));
        Prefill();
    }

    void Prefill()
    {
        var chosen = manual ? null : Candidates.FirstOrDefault(c => c.Selected)?.Candidate.Mapping;
        target = manual ? ingredient?.Id : chosen?.IngredientId;
        unit = rules is not null && line is not null && target is not null ? Scale.Of(rules, target) : null;
        pack = line is null ? null : PackSize.Read(line.Name);
        resolved = unit is null ? null : Factors.Of(rules!, target!, line!, null);
        stored = ShowFactor ? chosen?.Factor : null;
        prefilled = stored is { } s ? Format.Group(s)
            : resolved is (var f, var per, _) && f % per == 0 ? Format.Group(f / per) : "";
        Factor = prefilled;
        foreach (var name in (string[])[nameof(ShowFactor), nameof(NeedsFactor), nameof(ShowFields), nameof(FactorLabel), nameof(FactorUnit),
                     nameof(FactorHint), nameof(FactorOrigin), nameof(HasOrigin), nameof(Estimate)])
            Raise(name);
    }
}

public partial class MappingDetail : UserControl
{
    readonly MappingDetailModel model = new();
    Session? session;
    Func<CancellationToken> ct = () => new CancellationToken(true);
    int suggestSeq;

    public MappingDetail()
    {
        InitializeComponent();
        DataContext = model;
    }

    public event Action<LineGroup>? Assigned;

    public LineGroup? Group { get; private set; }

    public bool Blocked { set => model.Mapping = value; }

    Session Session => session ?? throw new InvalidOperationException("MappingDetail is not attached");
    CancellationToken Ct => ct();

    public void Attach(Session s, Func<CancellationToken> token)
    {
        session = s;
        ct = token;
    }

    public void Refresh()
    {
        IngredientBox.SetCategoryNames(this, Session.CategoryNames);
        IngredientBox.SetSimilar(this, Session.SimilarIngredients);
        model.Ingredients = Session.Ingredients();
    }

    public void Reset()
    {
        model.Assigned = "";
        Show(null);
    }

    public async void Show(LineGroup? g)
    {
        var seq = ++suggestSeq;
        if (g?.Key != Group?.Key) model.Assigned = "";
        Group = g;
        model.SetCandidates([]);
        model.ClearSnippets();
        if (g is null || Session.Case is null)
        {
            model.HasSelection = false;
            model.Select(null, null);
            return;
        }
        model.Title = g.Name;
        model.Supplier = g.Supplier;
        model.Article = g.Article ?? "";
        var total = Format.Quantity(g.Quantity, g.Unit);
        model.Total = g.Lines.Count == 1 ? total : total + " in " + g.Lines.Count + " Positionen";
        model.Current = g.MappingId is null || Session.Rules is null ? ""
            : Names.Mapping(Session.Rules, g.MappingId) + " · " + g.StateText.ToLowerInvariant();
        model.CurrentAuto = g.IsAutomatic;
        model.HasSelection = true;
        model.Loading = true;
        var (inv, line) = g.Lines[0];
        var lineItem = Session.Case.Invoices[inv].Lines[line];
        model.Select(Session.Rules, lineItem, g.Quantity);
        ShowSnippets(seq, g);
        await Session.Run(async () =>
        {
            var candidates = await Session.Service.SuggestMapping(Session.Case.Id, lineItem, g.Supplier, Ct);
            if (seq != suggestSeq) return;
            model.Loading = false;
            model.SetCandidates(Rows(candidates, g.MappingId));
        });
        if (seq == suggestSeq) model.Loading = false;
    }

    // Each position the group was built from, in the order they were delivered. The rows are there at
    // once; their excerpts are cut in parallel, each from its own page.
    async void ShowSnippets(int seq, LineGroup g)
    {
        var k = Session.Case!;
        var rows = g.Lines.Select(p => new SnippetRow(k.Invoices[p.Invoice], p.Line)).OrderBy(r => r.Invoice.Date).ToList();
        model.SetSnippets(rows);
        await Task.WhenAll(rows.Select(async row =>
        {
            var (image, text) = await Snippet(k.Id, row.Invoice, row.Line);
            if (seq == suggestSeq) row.Show(image, text);
            else image?.Dispose();
        }));
    }

    async Task<(Bitmap?, string?)> Snippet(string caseId, Invoice inv, int index)
    {
        var line = inv.Lines[index];
        try
        {
            if (inv.Source == Source.Scan)
                return (await Session.Service.InvoiceSnippet(caseId, inv.Id, index, line.Name, Ct) is { } row ? Images.From(row) : null, null);
            if (inv.Source is Source.Ubl or Source.Cii)
            {
                if (!Session.Sources.TryGetValue(inv.Id, out var src))
                    Session.Sources[inv.Id] = src = await Session.Service.InvoiceSource(caseId, inv.Id, Ct);
                if (src.Pages.FirstOrDefault()?.Text is { } xml) return (null, Ui.Snippet.Excerpt(xml, index, line));
            }
        }
        catch (Exception e) when (e is ServiceError or OperationCanceledException)
        {
        }
        return (null, null);
    }

    List<CandidateRow> Rows(List<MappingCandidate> candidates, string? current)
    {
        if (Session.Rules is not { } rs || Session.Case is not { } k) return [];
        var used = UsedIn(k, Recipes.Effective(k, rs));
        return candidates.Select(c => new CandidateRow(c, Names.Candidate(rs, c.Mapping), c.Mapping.Id != "" && c.Mapping.Id == current,
            used.GetValueOrDefault(c.Mapping.IngredientId, ""))).ToList();
    }

    static Dictionary<string, string> UsedIn(Case k, RuleSet rs) =>
        k.Products.Select(cp => rs.Products.GetValueOrDefault(cp.ProductId)).OfType<Product>()
            .SelectMany(p => p.Recipe.Select(l => (l.IngredientId, p.Name)).Distinct())
            .GroupBy(x => x.IngredientId)
            .ToDictionary(g => g.Key, g => string.Join(", ", g.Select(x => x.Name).Order(StringComparer.Ordinal)));

    void OpenInvoice(object? sender, RoutedEventArgs e)
    {
        if ((sender as Control)?.DataContext is SnippetRow row) Session.OpenInvoice(row.Invoice.Id);
    }

    void ToggleManual(object? sender, RoutedEventArgs e) => model.Manual = !model.Manual;

    // Whatever the group carried before is replaced under the same id: the rule for this
    // article is corrected, not joined by a second one the exact match could pick instead.
    async void Assign(object? sender, RoutedEventArgs e)
    {
        if (Group is not { } g || Session.Case is null) return;
        if (!model.Manual)
        {
            if (model.Candidates.FirstOrDefault(c => c.Selected) is not { } chosen || !ReadFactor(g, out var packed, out var piece)) return;
            var suggested = chosen.Candidate.Mapping;
            if (!await Weigh(suggested.IngredientId, piece)) return;
            if (suggested.Id == "" || !suggested.Confirmed || model.ShowFactor && suggested.Factor != packed)
            {
                if (suggested.Id == "")
                {
                    suggested.Id = g.MappingId ?? Session.NewId("map");
                    suggested.UnitCode = g.Unit;
                }
                if (model.ShowFactor) suggested.Factor = packed;
                suggested.Confirmed = true;
                if (!await Session.Put(suggested, Ct)) return;
            }
            await AssignId(g, suggested.Id, Names.Candidate(Session.Rules!, suggested));
            return;
        }
        if (model.Ingredient is null)
        {
            Session.Fail("Bitte eine Zutat wählen.");
            return;
        }
        if (!ReadFactor(g, out var factor, out var weight) || !await Weigh(model.Ingredient.Id, weight)) return;
        var mapping = new ArticleMapping
        {
            Id = g.MappingId ?? Session.NewId("map"),
            SupplierName = g.Supplier,
            SupplierArticleId = g.Article,
            Name = g.Name,
            Observed = g.Name,
            UnitCode = g.Unit,
            IngredientId = model.Ingredient.Id,
            Factor = factor,
            Confirmed = true,
        };
        if (await Session.Put(mapping, Ct)) await AssignId(g, mapping.Id, Names.Candidate(Session.Rules!, mapping));
    }

    bool ReadFactor(LineGroup g, out long? factor, out Piece? piece)
    {
        if (model.Decide(out factor, out piece)) return true;
        Session.Fail($"{Units.Label(g.Unit)} lässt sich nicht umrechnen — bitte den Inhalt je {Units.Label(g.Unit)} angeben.");
        return false;
    }

    async Task<bool> Weigh(string ingredientId, Piece? piece)
    {
        if (piece is null) return true;
        if (Session.Rules?.Ingredients.GetValueOrDefault(ingredientId) is not { } i) return false;
        return await Session.Put(new Ingredient { Id = i.Id, Name = i.Name, CategoryId = i.CategoryId, Aliases = [.. i.Aliases], Piece = piece }, Ct);
    }

    async Task AssignId(LineGroup g, string mappingId, string label)
    {
        if (Session.Case is null || mappingId == "") return;
        foreach (var (inv, line) in g.Lines) Session.Case.Invoices[inv].Lines[line].MappingId = mappingId;
        if (!await Session.SaveCase(Ct)) return;
        model.Assigned = "„" + g.Name + "“ ist jetzt " + label + " zugeordnet.";
        Assigned?.Invoke(g);
    }
}

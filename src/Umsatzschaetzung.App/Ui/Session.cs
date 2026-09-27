using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Styling;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Umsatzschaetzung.App.Ui;

public sealed record PickedFile(string Name, byte[] Data);

// A picked file, read only once its turn comes: a browser hands out its files without a path.
public sealed record FileSource(string Name, Func<CancellationToken, Task<byte[]>> Read)
{
    public static FileSource Of(string path) => new(Path.GetFileName(path), ct => File.ReadAllBytesAsync(path, ct));

    public static FileSource Of(IStorageFile file) => file.TryGetLocalPath() is { } path ? Of(path) : new(file.Name, async ct =>
    {
        using (file)
        {
            await using var stream = await file.OpenReadAsync();
            var data = new byte[stream.Length];
            await stream.ReadExactlyAsync(data, ct);
            return data;
        }
    });
}

public enum Tab { Case, Invoices, Mapping, Products, Calc, Report }

public enum SaveState { Idle, Slow, Stuck }

public sealed class Session : Observable
{
    // A browser's dialog filters by MIME type alone.
    public static readonly FilePickerFileType[] InvoiceFilter =
    [
        new("Rechnungen")
        {
            Patterns = ["*.xml", "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff"],
            MimeTypes = ["application/xml", "application/pdf", "image/png", "image/jpeg", "image/tiff"],
        },
        new("Alle Dateien") { Patterns = ["*"] },
    ];
    public static readonly FilePickerFileType[] CaseFilter = [new("Prüfung") { Patterns = ["*.db"], MimeTypes = ["application/vnd.sqlite3"] }];
    public static readonly FilePickerFileType[] PdfFilter = [new("PDF") { Patterns = ["*.pdf"], MimeTypes = ["application/pdf"] }];
    public static readonly FilePickerFileType[] CsvFilter = [new("CSV") { Patterns = ["*.csv"], MimeTypes = ["text/csv"] }];

    static readonly Dictionary<string, string> NoCategories = [];

    static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1), StuckAfter = TimeSpan.FromSeconds(10);

    readonly List<Write> pending = [];
    Task? draining;
    SaveState saveState;
    string error = "";
    Dictionary<string, string> recorded = [];
    bool stored = true;
    int stores;

    public Session(Services service)
    {
        Service = service;
        Imports = new Imports(this);
        History = new History(this);
        RulesHistory = new History(this);
    }

    // The main window keeps what was changed there, on the case and on the rules; the rules window keeps its own.
    public History History { get; }
    public History RulesHistory { get; }

    // Session has no visual of its own; the shell hands it its top level so the file pickers have a parent.
    public TopLevel? Owner { get; set; }

    // Answers the next file dialog in place of the person, where there is none to show.
    public Func<IEnumerable<string>>? Picked { get; set; }

    public Services Service { get; }
    public Imports Imports { get; }
    public Case? Case { get; private set; }
    public RuleSet? Rules { get; private set; }
    public StatusResp? Status { get; private set; }
    // What a scan was read as, freshly from an import or fetched back from the case it was stored with.
    public Dictionary<string, OcrResp> Readings { get; } = [];
    public Dictionary<string, InvoiceSourceResp> Sources { get; } = [];

    // The window, or the frame, last worked in.
    public object? ActiveWindow { get; set; }
    public object? ErrorWindow { get; private set; }

    public string Error
    {
        get => error;
        set
        {
            ErrorWindow = value == "" ? null : ActiveWindow;
            Set(ref error, value);
        }
    }

    // An error belongs to the frame it happened in; only that one shows it, and closing it drops it.
    public void Anchor(Frame frame, Control banner, TextBlock text)
    {
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Error)) return;
            text.Text = error;
            banner.IsVisible = ErrorWindow == frame;
        }
        PropertyChanged += Changed;
        frame.Activated += () => ActiveWindow = frame;
        frame.Closed += () =>
        {
            PropertyChanged -= Changed;
            if (ActiveWindow == frame) ActiveWindow = null;
            if (ErrorWindow == frame) Error = "";
        };
    }

    public event Action? CaseChanged, RulesChanged, StatusChanged, CaseClosed, RulesRequested;

    public void ShowRules() => RulesRequested?.Invoke();
    public event Action<string, Action<string>>? ProductRequested;

    public void NewProduct(string name, Action<string> created) => ProductRequested?.Invoke(name, created);
    public event Action<string, List<RecipeLine>?>? ProductEditRequested;

    // With a recipe the catalog form opens prefilled with it, ready to be saved.
    public void EditProduct(string productId, List<RecipeLine>? recipe = null) => ProductEditRequested?.Invoke(productId, recipe);

    // The Kalkulation takes it on its next result and selects that product.
    public string? WantedProduct { get; set; }

    public void OpenProduct(string productId)
    {
        WantedProduct = productId;
        Go(Tab.Calc);
    }
    public event Action<Case>? CaseOpened;
    public event Action<Tab>? TabRequested;
    public event Action<string>? InvoiceRequested;

    public void Go(Tab tab) => TabRequested?.Invoke(tab);

    public void OpenInvoice(string id) => InvoiceRequested?.Invoke(id);

    public async Task<bool> Run(Func<Task> work)
    {
        try
        {
            await work();
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (ServiceError e)
        {
            Fail(e);
            return false;
        }
    }

    public void Fail(string text) => Error = text;

    public void Fail(ServiceError e)
    {
        Error = e.Code switch
        {
            ErrorCode.Invalid => "Ungültige Eingabe: " + e.Message,
            ErrorCode.NotFound => "Nicht gefunden: " + e.Message,
            ErrorCode.Conflict => "Konflikt: " + e.Message,
            ErrorCode.Unavailable => "Regeldatenbank nicht erreichbar: " + e.Message,
            ErrorCode.Unsupported => "Nicht unterstützt: " + e.Message,
            _ => "Fehler: " + e.Message,
        };
        if (e.Code == ErrorCode.Unavailable) _ = LoadStatus(CancellationToken.None);
    }

    public Task LoadStatus(CancellationToken ct) => Run(async () =>
    {
        Status = await Service.Rules.Status(ct);
        StatusChanged?.Invoke();
    });

    public Task<bool> LoadRules(CancellationToken ct) => Run(async () =>
    {
        Rules = await Service.Rules.Load(ct);
        RulesChanged?.Invoke();
    });

    public void Open(Case kase)
    {
        History.Clear();
        SetCase(kase);
        CaseOpened?.Invoke(kase);
        _ = Warm(kase.Id);
    }

    // The matcher's model takes seconds to open in the browser; it opens with the case instead of
    // with the first line asked about. What fails here fails again there, where it is shown.
    async Task Warm(string caseId)
    {
        try
        {
            await Service.Mapping.Warm(caseId, CancellationToken.None);
        }
        catch (ServiceError)
        {
        }
    }

    // A case the service hands back is taken as it is, not as a change of this window.
    public void SetCase(Case kase)
    {
        Case = kase;
        recorded = CaseParts.Of(kase);
        CaseChanged?.Invoke();
    }

    // An invoice the service stored is taken into the case with what storing it changed; the rest
    // of the case stays as this window has it.
    public void Take(string caseId, Invoice invoice, Stored stored)
    {
        if (Case is not { } kase || kase.Id != caseId) return;
        var i = kase.Invoices.FindIndex(x => x.Id == invoice.Id);
        if (i >= 0) kase.Invoices[i] = invoice;
        else kase.Invoices.Add(invoice);
        foreach (var (id, m) in stored.Mappings) kase.Mappings[id] = m;
        kase.MappedStore = stored.MappedStore;
        CaseParts.Take(recorded, kase, invoice, stored.Mappings.Count > 0);
        CaseChanged?.Invoke();
    }

    public void CloseCase()
    {
        Case = null;
        recorded = [];
        History.Clear();
        CaseClosed?.Invoke();
    }

    public string Period => Case is null ? "" : Format.Period(Case.PeriodFrom, Case.PeriodTo);

    public SaveState SaveState
    {
        get => saveState;
        private set => Set(ref saveState, value);
    }

    public Task Saved => draining ?? Task.CompletedTask;

    // What changed on the case since it was last recorded is recorded where it was made.
    public Task<bool> SaveCase(Place at, CancellationToken ct)
    {
        if (Case is not { } kase) return Task.FromResult(false);
        var now = CaseParts.Of(kase);
        var change = CaseChange.Between(this, recorded, now);
        if (change is not null) at.History.Record(at, change);
        recorded = now;
        return change is null && stored ? Task.FromResult(true) : Store(kase, ct);
    }

    // Undo and redo put the case back without that being a change of its own.
    public Task<bool> Restore(Case kase)
    {
        SetCase(kase);
        return Store(kase, CancellationToken.None);
    }

    // Nothing changed since the last write went through: there is nothing to write.
    Task<bool> Store(Case kase, CancellationToken ct)
    {
        var n = ++stores;
        stored = false;
        var write = Enqueue(kase, async () =>
        {
            var saved = await Service.Cases.Put(Json.Copy(kase), CancellationToken.None);
            kase.CreatedAt = saved.CreatedAt;
            kase.UpdatedAt = saved.UpdatedAt;
            if (Case == kase) CaseChanged?.Invoke();
        }, CancellationToken.None);
        _ = Settled(write, n);
        return ct.CanBeCanceled ? Outcome(write, ct) : write;
    }

    async Task Settled(Task<bool> write, int n)
    {
        if (await write && n == stores) stored = true;
    }

    public async Task<bool> Put(IRuleEntity data, Place at, CancellationToken ct)
    {
        var kind = RuleChange.KindOf(data);
        var before = Rules?.Find(kind, data.Id) is { } old ? Json.Copy(old) : null;
        var after = Json.Copy(data);
        after.Meta = new Meta { ChangedAt = Clock.Now() };
        if (!await Store(after, ct)) return false;
        at.History.Record(at, new RuleChange(this, kind, data.Id, before, after));
        return true;
    }

    public async Task<bool> Delete(Entity entity, string id, Place at, CancellationToken ct)
    {
        var before = Rules?.Find(entity, id) is { } old ? Json.Copy(old) : null;
        if (!await Drop(entity, id, ct)) return false;
        at.History.Record(at, new RuleChange(this, entity, id, before, null));
        return true;
    }

    // Undo and redo put a rule back as it was, or take it away again, without that being a change of its own.
    public Task<bool> Restore(Entity entity, string id, IRuleEntity? rule)
    {
        if (rule is null) return Drop(entity, id, CancellationToken.None);
        var data = Json.Copy(rule);
        data.Meta.ChangedAt = Clock.Now();
        return Store(data, CancellationToken.None);
    }

    Task<bool> Store(IRuleEntity data, CancellationToken ct) =>
        Enqueue((data.GetType(), data.Id), async () =>
        {
            try
            {
                Rules = await Service.Rules.Save(Json.Copy(data), CancellationToken.None);
            }
            catch (ServiceError)
            {
                await LoadRules(CancellationToken.None);
                throw;
            }
            RulesChanged?.Invoke();
            await LoadStatus(CancellationToken.None);
        }, ct);

    Task<bool> Drop(Entity entity, string id, CancellationToken ct) =>
        Enqueue(null, async () =>
        {
            Rules = await Service.Rules.Delete(entity, id, CancellationToken.None);
            RulesChanged?.Invoke();
            await LoadStatus(CancellationToken.None);
        }, ct);

    // One write at a time, in the order asked. A write for the same thing as one still
    // waiting takes its place, unless a delete waits between them. Each write copies what
    // it stores when it starts, so edits made meanwhile never reach it half done.
    Task<bool> Enqueue(object? key, Func<Task> work, CancellationToken ct)
    {
        var i = pending.FindLastIndex(w => w.Key is null || w.Key.Equals(key));
        if (key is null || i < 0 || pending[i].Key is null)
        {
            i = pending.Count;
            pending.Add(new Write(key, work));
        }
        else pending[i].Work = work;
        var done = pending[i].Done.Task;
        if (draining is not { IsCompleted: false }) draining = Drain();
        return ct.CanBeCanceled ? Outcome(done, ct) : done;
    }

    static async Task<bool> Outcome(Task<bool> write, CancellationToken ct)
    {
        try
        {
            return await write.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    async Task Drain()
    {
        while (pending.Count > 0)
        {
            var write = pending[0];
            pending.RemoveAt(0);
            write.Done.SetResult(await Watched(Run(write.Work)));
        }
        SaveState = SaveState.Idle;
    }

    async Task<bool> Watched(Task<bool> write)
    {
        if (await Task.WhenAny(write, Task.Delay(SlowAfter)) == write) return await write;
        SaveState = SaveState.Slow;
        if (await Task.WhenAny(write, Task.Delay(StuckAfter - SlowAfter)) == write) return await write;
        SaveState = SaveState.Stuck;
        return await write;
    }

    // Every window that edits shows the same badge while a write takes long, until it stops as returned.
    public Action Indicate(Control window, Border badge, TextBlock text)
    {
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SaveState)) return;
            var stuck = saveState == SaveState.Stuck;
            badge.IsVisible = saveState != SaveState.Idle;
            badge.Theme = (ControlTheme)window.FindResource(stuck ? "BadgeWarning" : "Badge")!;
            text.Text = stuck ? "Speichern dauert ungewöhnlich lange" : "Speichert …";
            var tip = stuck
                ? "Die letzten Änderungen sind noch nicht gespeichert. Liegt der Speicher auf einem Netzlaufwerk, "
                    + "kann die Verbindung langsam oder unterbrochen sein. Das Speichern läuft weiter."
                : null;
            ToolTip.SetTip(badge, tip);
            AutomationProperties.SetHelpText(text, tip);
        }
        AutomationProperties.SetLiveSetting(text, AutomationLiveSetting.Polite);
        PropertyChanged += Changed;
        return () => PropertyChanged -= Changed;
    }

    sealed class Write(object? key, Func<Task> work)
    {
        public object? Key { get; } = key;
        public Func<Task> Work { get; set; } = work;
        public TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public List<Category> Categories() =>
        Rules is null ? [] : Rules.Categories.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();

    public IReadOnlyDictionary<string, string> CategoryNames =>
        Rules?.Categories.ToDictionary(kv => kv.Key, kv => kv.Value.Name) ?? NoCategories;

    public string CategoryName(string? id) =>
        Rules is not null && !string.IsNullOrEmpty(id) && Rules.Categories.TryGetValue(id, out var c) ? c.Name : "";

    public List<Gewerbezweig> Gewerbezweige() =>
        Rules is null ? [] : Rules.Gewerbezweige.Values.OrderBy(g => g.Kennzahl, StringComparer.Ordinal).ToList();

    public bool KnownGewerbe(string kennzahl) =>
        Rules is not null && Rules.Gewerbezweige.Values.Any(g => g.Kennzahl == kennzahl);

    public List<Ingredient> Ingredients() =>
        Rules is null ? [] : Rules.Ingredients.Values.OrderBy(i => i.Name, StringComparer.Ordinal).ToList();

    public async Task<IReadOnlyList<string>> SimilarIngredients(string text, CancellationToken ct) =>
        (await Service.Mapping.Suggest(Case?.Id ?? "", new InvoiceLine { Name = text }, null, ct))
            .Select(c => c.Mapping.IngredientId).ToList();

    public List<Product> Products() =>
        Rules is null ? [] : Rules.Products.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    public string IngredientName(string id) =>
        Rules is not null && Rules.Ingredients.TryGetValue(id, out var i) ? i.Name : "";

    public async Task<List<PickedFile>> PickFiles(IReadOnlyList<FilePickerFileType> filter, bool multi) =>
        await ReadFiles(await PickSources(filter, multi));

    public async Task<List<FileSource>> PickSources(IReadOnlyList<FilePickerFileType> filter, bool multi)
    {
        if (Picked is { } answer) return [.. answer().Select(FileSource.Of)];
        if (Owner?.StorageProvider is not { } storage) return [];
        return Files(await storage.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = multi, FileTypeFilter = filter }));
    }

    public static List<FileSource> Files(IEnumerable<IStorageItem> items) =>
        [.. items.OfType<IStorageFile>().Select(FileSource.Of)];

    public async Task<List<PickedFile>> ReadFiles(IEnumerable<FileSource> sources)
    {
        var files = new List<PickedFile>();
        foreach (var source in sources)
        {
            try
            {
                files.Add(new PickedFile(source.Name, await source.Read(CancellationToken.None)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Fail("Datei konnte nicht gelesen werden: " + source.Name);
            }
        }
        return files;
    }

    public async Task<string?> SaveFile(string name, byte[] data, IReadOnlyList<FilePickerFileType> filter)
    {
        if (Owner?.StorageProvider is not { } storage) return null;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions { SuggestedFileName = name, FileTypeChoices = filter });
        if (file is null) return null;
        try
        {
            await using var stream = await file.OpenWriteAsync();
            await stream.WriteAsync(data);
            return file.Name;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Fail("Datei konnte nicht gespeichert werden: " + file.Name);
            return null;
        }
    }
}

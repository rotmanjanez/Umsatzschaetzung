using System.ComponentModel;
using System.Security.Cryptography;
using Avalonia.Styling;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Umsatzschaetzung.App.Ui;

public sealed record PickedFile(string Name, byte[] Data);

public enum Tab { Case, Invoices, Mapping, Products, Calc, Report }

public enum SaveState { Idle, Slow, Stuck }

public sealed class Session : Observable
{
    public static readonly FilePickerFileType[] InvoiceFilter =
    [
        new("Rechnungen") { Patterns = ["*.xml", "*.pdf", "*.png", "*.jpg", "*.jpeg", "*.tif", "*.tiff"] },
        new("Alle Dateien") { Patterns = ["*"] },
    ];
    public static readonly FilePickerFileType[] CaseFilter = [new("Prüfung") { Patterns = ["*.db"] }];
    public static readonly FilePickerFileType[] PdfFilter = [new("PDF") { Patterns = ["*.pdf"] }];
    public static readonly FilePickerFileType[] CsvFilter = [new("CSV") { Patterns = ["*.csv"] }];

    static readonly Dictionary<string, string> NoCategories = [];

    static readonly TimeSpan SlowAfter = TimeSpan.FromSeconds(1), StuckAfter = TimeSpan.FromSeconds(10);

    readonly List<Write> pending = [];
    Task? draining;
    SaveState saveState;
    string error = "";

    public Session(IService service)
    {
        Service = service;
        Imports = new Imports(this);
    }

    // Session has no visual of its own; Shell assigns itself so the file pickers have a parent.
    public TopLevel? Owner { get; set; }

    // Answers the next file dialog in place of the person, where there is none to show.
    public Func<IEnumerable<string>>? Picked { get; set; }

    public IService Service { get; }
    public Imports Imports { get; }
    public Case? Case { get; private set; }
    public RuleSet? Rules { get; private set; }
    public StatusResp? Status { get; private set; }
    // What a scan was read as, freshly from an import or fetched back from the case it was stored with.
    public Dictionary<string, OcrResp> Readings { get; } = [];
    public Dictionary<string, InvoiceSourceResp> Sources { get; } = [];

    public Window? ActiveWindow { get; set; }
    public Window? ErrorWindow { get; private set; }

    public string Error
    {
        get => error;
        set
        {
            ErrorWindow = value == "" ? null : ActiveWindow;
            Set(ref error, value);
        }
    }

    // An error belongs to the window it happened in; only that one shows it, and closing it drops it.
    public void Anchor(Window window, Control banner, TextBlock text)
    {
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(Error)) return;
            text.Text = error;
            banner.IsVisible = ErrorWindow == window;
        }
        PropertyChanged += Changed;
        window.Activated += (_, _) => ActiveWindow = window;
        window.Closed += (_, _) =>
        {
            PropertyChanged -= Changed;
            if (ActiveWindow == window) ActiveWindow = null;
            if (ErrorWindow == window) Error = "";
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
        Status = await Service.Status(ct);
        StatusChanged?.Invoke();
    });

    public Task<bool> LoadRules(CancellationToken ct) => Run(async () =>
    {
        Rules = await Service.Rules(ct);
        RulesChanged?.Invoke();
    });

    public void Open(Case kase)
    {
        SetCase(kase);
        CaseOpened?.Invoke(kase);
    }

    public void SetCase(Case kase)
    {
        Case = kase;
        CaseChanged?.Invoke();
    }

    public void CloseCase()
    {
        Case = null;
        CaseClosed?.Invoke();
    }

    public string Period => Case is null ? "" : Format.Period(Case.PeriodFrom, Case.PeriodTo);

    public SaveState SaveState
    {
        get => saveState;
        private set => Set(ref saveState, value);
    }

    public Task Saved => draining ?? Task.CompletedTask;

    public Task<bool> SaveCase(CancellationToken ct)
    {
        if (Case is not { } kase) return Task.FromResult(false);
        return Enqueue(kase, async () =>
        {
            var saved = await Service.PutCase(Json.Copy(kase), CancellationToken.None);
            kase.CreatedAt = saved.CreatedAt;
            kase.UpdatedAt = saved.UpdatedAt;
            if (Case == kase) SetCase(kase);
        }, ct);
    }

    public Task<bool> Put(IRuleEntity data, CancellationToken ct)
    {
        data.Meta = new Meta { ChangedAt = Clock.Now() };
        return Enqueue((data.GetType(), data.Id), async () =>
        {
            try
            {
                Rules = await Service.SaveRule(Json.Copy(data), CancellationToken.None);
            }
            catch (ServiceError)
            {
                await LoadRules(CancellationToken.None);
                throw;
            }
            RulesChanged?.Invoke();
            await LoadStatus(CancellationToken.None);
        }, ct);
    }

    public Task<bool> Delete(Entity entity, string id, CancellationToken ct) =>
        Enqueue(null, async () =>
        {
            Rules = await Service.DeleteRule(entity, id, CancellationToken.None);
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

    // Every window that edits shows the same badge while a write takes long.
    public void Indicate(Window window, Border badge, TextBlock text)
    {
        void Changed(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(SaveState)) return;
            var stuck = saveState == SaveState.Stuck;
            badge.IsVisible = saveState != SaveState.Idle;
            badge.Theme = (ControlTheme)window.FindResource(stuck ? "BadgeWarning" : "Badge")!;
            text.Text = stuck ? "Speichern dauert ungewöhnlich lange" : "Speichert …";
            ToolTip.SetTip(badge, stuck
                ? "Die letzten Änderungen sind noch nicht gespeichert. Liegt der Speicher auf einem Netzlaufwerk, "
                    + "kann die Verbindung langsam oder unterbrochen sein. Das Speichern läuft weiter."
                : null);
        }
        PropertyChanged += Changed;
        window.Closed += (_, _) => PropertyChanged -= Changed;
    }

    sealed class Write(object? key, Func<Task> work)
    {
        public object? Key { get; } = key;
        public Func<Task> Work { get; set; } = work;
        public TaskCompletionSource<bool> Done { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public static string NewId(string prefix) => prefix + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

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
        (await Service.SuggestMapping(Case?.Id ?? "", new InvoiceLine { Name = text }, null, ct))
            .Select(c => c.Mapping.IngredientId).ToList();

    public List<Product> Products() =>
        Rules is null ? [] : Rules.Products.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    public string IngredientName(string id) =>
        Rules is not null && Rules.Ingredients.TryGetValue(id, out var i) ? i.Name : "";

    public async Task<List<PickedFile>> PickFiles(IReadOnlyList<FilePickerFileType> filter, bool multi)
    {
        if (Picked is { } answer) return await ReadFiles(answer());
        if (Owner?.StorageProvider is not { } storage) return [];
        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { AllowMultiple = multi, FileTypeFilter = filter });
        return await ReadFiles(picked.Select(f => f.TryGetLocalPath()).OfType<string>());
    }

    public async Task<List<PickedFile>> ReadFiles(IEnumerable<string> paths)
    {
        var files = new List<PickedFile>();
        foreach (var path in paths)
        {
            try
            {
                files.Add(new PickedFile(Path.GetFileName(path), await File.ReadAllBytesAsync(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Fail("Datei konnte nicht gelesen werden: " + Path.GetFileName(path));
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

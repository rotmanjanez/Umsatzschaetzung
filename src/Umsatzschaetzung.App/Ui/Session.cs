using System.Security.Cryptography;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace Umsatzschaetzung.App.Ui;

public sealed record PickedFile(string Name, byte[] Data);

public enum Tab { Case, Invoices, Mapping, Calc, Report }

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

    string error = "";

    public Session(IService service)
    {
        Service = service;
        Imports = new Imports(this);
    }

    // Session has no visual of its own; Shell assigns itself so the file pickers have a parent.
    public TopLevel? Owner { get; set; }

    public IService Service { get; }
    public Imports Imports { get; }
    public Case? Case { get; private set; }
    public RuleSet? Rules { get; private set; }
    public StatusResp? Status { get; private set; }
    // What a scan was read as, freshly from an import or fetched back from the case it was stored with.
    public Dictionary<string, OcrResp> Readings { get; } = [];
    public Dictionary<string, InvoiceSourceResp> Sources { get; } = [];

    public string Error { get => error; set => Set(ref error, value); }

    public event Action? CaseChanged, RulesChanged, StatusChanged, CaseClosed, RulesRequested;

    public void ShowRules() => RulesRequested?.Invoke();
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

    public Task<bool> SaveCase(CancellationToken ct)
    {
        var kase = Case;
        if (kase is null) return Task.FromResult(false);
        return Run(async () =>
        {
            var saved = await Service.PutCase(kase, ct);
            if (Case == kase) SetCase(saved);
        });
    }

    public Task<bool> Put(IRuleEntity data, CancellationToken ct)
    {
        data.Meta = new Meta { ChangedAt = Clock.Now() };
        return SaveRule(data, ct);
    }

    public Task<bool> Delete(Entity entity, string id, CancellationToken ct) =>
        Run(async () =>
        {
            Rules = await Service.DeleteRule(entity, id, ct);
            RulesChanged?.Invoke();
            await LoadStatus(ct);
        });

    Task<bool> SaveRule(IRuleEntity rule, CancellationToken ct) =>
        Run(async () =>
        {
            try
            {
                Rules = await Service.SaveRule(rule, ct);
            }
            catch (ServiceError)
            {
                await LoadRules(ct);
                throw;
            }
            RulesChanged?.Invoke();
            await LoadStatus(ct);
        });

    public static string NewId(string prefix) => prefix + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

    public List<Category> Categories() =>
        Rules is null ? [] : Rules.Categories.Values.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();

    public IReadOnlyDictionary<string, string> CategoryNames =>
        Rules?.Categories.ToDictionary(kv => kv.Key, kv => kv.Value.Name) ?? NoCategories;

    public string CategoryName(string? id) =>
        Rules is not null && !string.IsNullOrEmpty(id) && Rules.Categories.TryGetValue(id, out var c) ? c.Name : "";

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

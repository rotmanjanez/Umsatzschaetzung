using System.IO;
using System.Security.Cryptography;
using Umsatzschätzung.Model;
using Umsatzschätzung.Service;
using Microsoft.Win32;

namespace Umsatzschätzung.App.Ui;

public sealed record PickedFile(string Name, byte[] Data);

public sealed class Session : Observable
{
    public const string InvoiceFilter = "Rechnungen|*.xml;*.pdf;*.png;*.jpg;*.jpeg;*.tif;*.tiff|Alle Dateien|*.*";
    public const string CaseFilter = "Prüfung|*.json";
    public const string PdfFilter = "PDF|*.pdf";
    public const string CsvFilter = "CSV|*.csv";

    string message = "";

    public Session(IService service) => Service = service;

    public IService Service { get; }
    public Case? Case { get; private set; }
    public CaseDisplay? Display { get; private set; }
    public RuleSetResp? Rules { get; private set; }
    public StatusResp? Status { get; private set; }
    public Dictionary<string, OcrResp> Drafts { get; } = [];

    public string Message { get => message; set => Set(ref message, value); }

    public event Action? CaseChanged, RulesChanged, StatusChanged, CaseClosed;
    public event Action<CaseResp>? CaseOpened;

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

    public void Fail(ServiceError e)
    {
        Message = e.Code switch
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

    public void Open(CaseResp resp)
    {
        SetCase(resp);
        CaseOpened?.Invoke(resp);
    }

    public void SetCase(CaseResp resp)
    {
        Case = resp.Case;
        Display = resp.Display;
        CaseChanged?.Invoke();
    }

    public void CloseCase()
    {
        Case = null;
        Display = null;
        CaseClosed?.Invoke();
    }

    public Task<bool> SaveCase(CancellationToken ct)
    {
        var kase = Case;
        if (kase is null) return Task.FromResult(false);
        return Run(async () =>
        {
            var resp = await Service.PutCase(kase, ct);
            if (Case == kase) SetCase(resp);
        });
    }

    public Task<bool> Put(IRuleEntity data, CancellationToken ct)
    {
        data.Meta = new Meta { ChangedAt = Clock.Now() };
        return SaveRule(data, ct);
    }

    public Task<bool> Retire(Entity entity, string id, CancellationToken ct)
    {
        if (Rules?.RuleSet.Find(entity, id) is not { } e) return Task.FromResult(false);
        e.Meta.ValidTo = DateOnly.FromDateTime(DateTime.Now);
        return SaveRule(e, ct);
    }

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

    public static string EntityLabel(Entity e) => e switch
    {
        Entity.Ingredient => "Zutat",
        Entity.Mapping => "Zuordnung",
        Entity.Product => "Produkt",
        _ => "Ertragsregel",
    };

    public static string NewId(string prefix) => prefix + "-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(6));

    public List<Ingredient> Ingredients() =>
        Rules is null ? [] : Rules.RuleSet.Ingredients.Values.OrderBy(i => i.Name, StringComparer.Ordinal).ToList();

    public List<Product> Products() =>
        Rules is null ? [] : Rules.RuleSet.Products.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    public string IngredientName(string id) =>
        Rules is not null && Rules.RuleSet.Ingredients.TryGetValue(id, out var i) ? i.Name : "";

    public async Task<List<PickedFile>> PickFiles(string filter, bool multi)
    {
        var dialog = new OpenFileDialog { Filter = filter, Multiselect = multi };
        if (dialog.ShowDialog() != true) return [];
        return await ReadFiles(dialog.FileNames);
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
                Message = "Datei konnte nicht gelesen werden: " + Path.GetFileName(path);
            }
        }
        return files;
    }

    public async Task SaveFile(string name, byte[] data, string filter)
    {
        var dialog = new SaveFileDialog { FileName = name, Filter = filter };
        if (dialog.ShowDialog() != true) return;
        try
        {
            await File.WriteAllBytesAsync(dialog.FileName, data);
            Message = "Gespeichert: " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Message = "Datei konnte nicht gespeichert werden: " + Path.GetFileName(dialog.FileName);
        }
    }
}

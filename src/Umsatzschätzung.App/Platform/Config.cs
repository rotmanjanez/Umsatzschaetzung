using System.Text.Json;

namespace Umsatzschätzung.App.Platform;

public sealed record Config(string Store, string CaseDir);

public static class AppData
{
    // .NET maps LocalApplicationData to ~/.local/share on macOS; the native
    // location for an app's own state there is ~/Library/Application Support.
    public static string Dir { get; } = OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Umsatzschätzung")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "Umsatzschätzung");
}

public static class AppConfig
{
    static readonly Config Defaults = new(
        Path.Combine(AppData.Dir, "store"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Umsatzschätzung"));

    public static Config Load()
    {
        if (OperatingSystem.IsWindows()) return RegistryConfig.Load();
        return File();
    }

    static Config File()
    {
        var path = Path.Combine(AppData.Dir, "config.json");
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            var file = JsonSerializer.Deserialize<Config>(stream, Options);
            return file is null ? Defaults : new(Full(file.Store) ?? Defaults.Store, Full(file.CaseDir) ?? Defaults.CaseDir);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return Defaults; }
    }

    static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    static string? Full(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
            : value.StartsWith('~') ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), value[1..].TrimStart('/'))
            : value;
}

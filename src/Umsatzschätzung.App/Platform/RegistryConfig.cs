using System.IO;
using Microsoft.Win32;

namespace Umsatzschätzung.App.Platform;

public sealed record Config(string Store, string CaseDir);

public static class RegistryConfig
{
    static readonly (RegistryKey Hive, string Path)[] Sources =
    [
        (Registry.LocalMachine, @"SOFTWARE\Policies\Umsatzschätzung"),
        (Registry.LocalMachine, @"SOFTWARE\Umsatzschätzung"),
        (Registry.CurrentUser, @"SOFTWARE\Umsatzschätzung"),
    ];

    public static Config Load() => new(
        Value("Store") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Umsatzschätzung", "store"),
        Value("CaseDir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Umsatzschätzung"));

    static string? Value(string name)
    {
        foreach (var (hive, path) in Sources)
        {
            using var key = hive.OpenSubKey(path);
            if (key?.GetValue(name) is string s && s.Length > 0)
                return s;
        }
        return null;
    }
}

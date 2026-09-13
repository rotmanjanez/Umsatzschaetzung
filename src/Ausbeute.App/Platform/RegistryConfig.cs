using System.IO;
using Microsoft.Win32;

namespace Ausbeute.App.Platform;

public sealed record Config(string Store, string CaseDir, string ModelDir);

public static class RegistryConfig
{
    static readonly (RegistryKey Hive, string Path)[] Sources =
    [
        (Registry.LocalMachine, @"SOFTWARE\Policies\Ausbeute"),
        (Registry.LocalMachine, @"SOFTWARE\Ausbeute"),
        (Registry.CurrentUser, @"SOFTWARE\Ausbeute"),
    ];

    public static Config Load() => new(
        Value("Store") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ausbeute", "store"),
        Value("CaseDir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents", "Ausbeute"),
        Value("ModelDir") ?? Path.Combine(AppContext.BaseDirectory, "models"));

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

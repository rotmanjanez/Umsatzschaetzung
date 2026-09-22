using Umsatzschaetzung.Model;

namespace Umsatzschaetzung;

static class TestData
{
    public static string Dir { get; } = Path.Combine(AppContext.BaseDirectory, "data");

    public static string Repo { get; } = FindRepo();

    public static string File(string name) => Path.Combine(Dir, name);

    public static string Fixture(string relative) => Path.Combine(Repo, "fixtures", relative);

    public static RuleSet Seed() => Json.Deserialize<RuleSet>(System.IO.File.ReadAllBytes(File("ruleset.json")));

    static string FindRepo()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !System.IO.File.Exists(Path.Combine(dir.FullName, "Umsatzschaetzung.slnx")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Umsatzschaetzung.slnx");
    }
}

sealed class TempDir : IDisposable
{
    public string Path { get; } = Directory.CreateTempSubdirectory("umsatzschätzung-test-").FullName;

    public string Sub(string name) => System.IO.Path.Combine(Path, name);

    public void Dispose()
    {
        try { Directory.Delete(Path, true); } catch (IOException) { }
    }
}

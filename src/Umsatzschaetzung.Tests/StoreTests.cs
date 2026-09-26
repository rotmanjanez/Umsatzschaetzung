using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Tests.Casefile;
using Umsatzschaetzung.Tests.Rulestore;

namespace Umsatzschaetzung;

// Schema 1 ist ausgeliefert: data/schema1 hält, was sein Schritt anlegt, und je eine Datei, die
// eine Version mit Schema 1 geschrieben hat. Ändert sich eins davon, gehört das in einen neuen Schritt.
public class StoreTests
{
    static string Frozen(string name) => TestData.File("schema1/" + name);

    static string Tables(string path)
    {
        using var db = Sql.Open(path);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name";
        using var r = cmd.ExecuteReader();
        var lines = new List<string>();
        while (r.Read()) lines.Add(r.GetString(0) + ";\n");
        return string.Concat(lines);
    }

    static string Text(string name) => File.ReadAllText(Frozen(name)).ReplaceLineEndings("\n");

    [Fact]
    public void ANewRuleStoreHasTheTablesOfSchema1()
    {
        using var tmp = new TempDir();
        new RuleStore(tmp.Path, TestData.Seed());

        Assert.Equal(1, Sql.UserVersion(tmp.Sub("rules.db")));
        Assert.Equal(Text("rules.sql"), Tables(tmp.Sub("rules.db")));
    }

    [Fact]
    public void ANewCaseFileHasTheTablesOfSchema1()
    {
        using var tmp = new TempDir();
        new CaseStore(tmp.Path).Save(Cases.Minimal("fall-1"));

        Assert.Equal(1, Sql.UserVersion(tmp.Sub("fall-1.db")));
        Assert.Equal(Text("case.sql"), Tables(tmp.Sub("fall-1.db")));
    }

    [Fact]
    public void ARuleStoreWrittenWithSchema1OpensWithItsChanges()
    {
        using var tmp = new TempDir();
        File.Copy(Frozen("rules.db"), tmp.Sub("rules.db"));

        var rules = new RuleStore(tmp.Path, TestData.Seed()).Load();

        Assert.Equal(1, rules.Version);
        Assert.Equal(new DateOnly(2024, 6, 30), rules.Products["prod.korn.4cl"].Meta.ValidTo);
        Assert.Equal(1, Sql.UserVersion(tmp.Sub("rules.db")));
    }

    // Die Regel-Datenbank liegt auf Freigaben, wo WAL nicht funktioniert.
    [Fact]
    public void TheRuleStoreKeepsTheRollbackJournal()
    {
        using var tmp = new TempDir();
        var store = new RuleStore(tmp.Path, TestData.Seed());
        store.Save(store.Load().Products["prod.korn.4cl"]);

        Assert.Equal("delete", Sql.Scalar(tmp.Sub("rules.db"), "PRAGMA journal_mode"));
        Assert.False(File.Exists(tmp.Sub("rules.db-wal")));
    }

    [Fact]
    public void ACaseFileWrittenWithSchema1OpensAsItWasSaved()
    {
        using var tmp = new TempDir();
        File.Copy(Frozen("case.db"), tmp.Sub(CaseStore.FileName("Schema 1")));
        var store = new CaseStore(tmp.Path);

        var expected = Cases.Full("fall-1");
        expected.Label = "Schema 1";
        Cases.Same(expected, store.Load("fall-1"));
        Cases.HoldsFile(store, "fall-1", "re-2", "Scan März.jpg", [1, 2, 3]);
        Assert.Equal(1, Sql.UserVersion(tmp.Sub(CaseStore.FileName("Schema 1"))));
    }
}

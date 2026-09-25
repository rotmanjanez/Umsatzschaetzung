using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Rulestore;

namespace Umsatzschaetzung.Tests.Casefile;

public class CaseTransferTests
{
    [Fact]
    public void ACaseExportsAsOneSqliteFileAndImportsElsewhereWithItsDocument()
    {
        using var tmp = new TempDir();
        var from = new CaseStore(tmp.Sub("a"));
        from.Save(Cases.Full("fall-1"));
        from.SaveFile("fall-1", "re-1", "rheinland.pdf", [1, 2, 3]);

        var data = from.Export("fall-1");
        var to = new CaseStore(tmp.Sub("b"));
        var imported = to.Import(data, false);

        Assert.Equal("SQLite format 3\0"u8.ToArray(), data[..16]);
        Cases.Same(Cases.Full("fall-1"), imported);
        Cases.Same(Cases.Full("fall-1"), to.Load("fall-1"));
        Cases.HoldsFile(to, "fall-1", "re-1", "rheinland.pdf", [1, 2, 3]);
        Assert.Empty(Directory.GetFiles(tmp.Sub("b"), ".*"));
    }

    [Fact]
    public void TheFixtureTravelsWithItsInvoice()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        var c = Cases.Fixture(store, tmp.Path);

        var back = new CaseStore(tmp.Sub("anderswo")).Import(store.Export(c.Id), false);

        Cases.Same(c, back);
    }

    [Fact]
    public void AnExistingCaseIsNotReplacedUnaskedAndTheConflictNamesItsLabel()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1", "Alt"));
        var other = new CaseStore(tmp.Sub("b"));
        other.Save(Cases.Minimal("fall-1", "Neu"));
        var data = other.Export("fall-1");

        var e = Assert.Throws<CaseExistsException>(() => store.Import(data, false));

        Assert.Equal("Alt", e.Label);
        Assert.Equal("Alt", store.Load("fall-1").Label);
        Assert.Empty(Directory.GetFiles(tmp.Path, ".*"));
    }

    [Fact]
    public void OverwriteReplacesAnExistingCase()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1", "Alt"));
        store.SaveFile("fall-1", "re-1", "alt.pdf", [9]);
        var other = new CaseStore(tmp.Sub("b"));
        other.Save(Cases.Full("fall-1"));

        var back = store.Import(other.Export("fall-1"), true);

        Cases.Same(Cases.Full("fall-1"), back);
        Cases.Same(Cases.Full("fall-1"), store.Load("fall-1"));
        Assert.Throws<CaseNotFoundException>(() => store.LoadFile("fall-1", "re-1"));
    }

    [Theory]
    [InlineData(new byte[] { 1, 2, 3 })]
    [InlineData(new byte[0])]
    public void AFileThatIsNotACaseIsRefused(byte[] data)
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);

        Assert.Throws<CaseInvalidException>(() => store.Import(data, false));
        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public void ADatabaseWithoutACaseIsRefused()
    {
        using var tmp = new TempDir();
        var path = tmp.Sub("leer.db");
        Sql.Exec(path, "CREATE TABLE x(a)");

        Assert.Throws<CaseInvalidException>(() => new CaseStore(tmp.Sub("cases")).Import(File.ReadAllBytes(path), false));
        Assert.Empty(Directory.GetFiles(tmp.Sub("cases")));
    }

    [Fact]
    public void ACaseFileFromANewerProgramIsRefusedOnImport()
    {
        using var tmp = new TempDir();
        var other = new CaseStore(tmp.Sub("b"));
        other.Save(Cases.Minimal("fall-1"));
        Sql.Exec(tmp.Sub("b/fall-1.db"), "PRAGMA user_version = 99");

        Assert.Throws<CaseInvalidException>(() => new CaseStore(tmp.Path).Import(File.ReadAllBytes(tmp.Sub("b/fall-1.db")), false));
        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public void ACaseFileHoldingAnInvalidCaseIsRefusedOnImport()
    {
        using var tmp = new TempDir();
        var other = new CaseStore(tmp.Sub("b"));
        other.Save(Cases.Minimal("fall-1"));
        Sql.Exec(tmp.Sub("b/fall-1.db"), "UPDATE kase SET label = ''");

        Assert.Throws<CaseInvalidException>(() => new CaseStore(tmp.Path).Import(File.ReadAllBytes(tmp.Sub("b/fall-1.db")), false));
    }

    [Fact]
    public void AnExportLeavesOutTheDocumentOfADeletedInvoice()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Sub("a"));
        var c = Cases.Full("fall-1");
        store.Save(c);
        store.SaveFile("fall-1", "re-1", "rheinland.pdf", [1, 2, 3]);
        store.SaveFile("fall-1", "re-2", "scan.jpg", [4, 5, 6]);
        c.Invoices.RemoveAll(i => i.Id == "re-2");
        store.Save(c);

        var to = new CaseStore(tmp.Sub("b"));
        to.Import(store.Export("fall-1"), false);

        Cases.HoldsFile(to, "fall-1", "re-1", "rheinland.pdf", [1, 2, 3]);
        Assert.Throws<CaseNotFoundException>(() => to.LoadFile("fall-1", "re-2"));
        Assert.Equal("scan.jpg", store.LoadFile("fall-1", "re-2").Name);
    }

    [Fact]
    public void ExportLeavesNoTemporaryFileBehind()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        store.Export("fall-1");

        Assert.Equal([tmp.Sub("fall-1.db")], Directory.GetFiles(tmp.Path));
    }
}

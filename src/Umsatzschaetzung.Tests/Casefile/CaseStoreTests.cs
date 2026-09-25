using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Tests.Rulestore;

namespace Umsatzschaetzung.Tests.Casefile;

public class CaseStoreTests
{
    [Fact]
    public void TheFixtureLoadsAsACaseWithOneInvoice()
    {
        using var tmp = new TempDir();
        var c = Cases.Fixture(new CaseStore(tmp.Path), tmp.Path);

        Assert.Equal(Cases.FixtureId, c.Id);
        Assert.Equal("Schankwirtschaft Zum Alten Fass, Bp 2024", c.Label);
        var inv = Assert.Single(c.Invoices);
        Assert.Equal(Source.Zugferd, inv.Source);
        Assert.Equal(["map.fass50", "map.kiste24x033", "map.korn07"], inv.Lines.Select(l => l.MappingId));
        Assert.Equal(3, c.Inventory.Count);
        Assert.Equal(5, c.Products.Count);
    }

    [Fact]
    public void EveryFieldOfACaseRoundTripsExactly()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Full("fall-1"));

        Cases.Same(Cases.Full("fall-1"), store.Load("fall-1"));
    }

    [Fact]
    public void AReopenedStoreReadsWhatAnEarlierOneWrote()
    {
        using var tmp = new TempDir();
        new CaseStore(tmp.Path).Save(Cases.Full("fall-1"));

        Cases.Same(Cases.Full("fall-1"), new CaseStore(tmp.Path).Load("fall-1"));
    }

    [Fact]
    public void SavingAgainReplacesTheCaseInsteadOfAddingToIt()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Full("fall-1"));
        var changed = Cases.Minimal("fall-1", "Neu");
        changed.Invoices = [new Invoice { Id = "re-9", Lines = [new InvoiceLine { No = 1, Name = "X" }] }];
        store.Save(changed);

        Cases.Same(changed, store.Load("fall-1"));
    }

    [Fact]
    public void ACaseInADirectoryThatDoesNotExistYetIsSaved()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Sub("a/b"));
        store.Save(Cases.Minimal("fall-1"));

        Assert.Equal("fall-1", store.Load("fall-1").Id);
    }

    [Fact]
    public void ListShowsTheMostRecentlyUpdatedCaseFirst()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        foreach (var (id, day) in new[] { ("b", 1), ("c", 3), ("a", 1) })
        {
            var c = Cases.Minimal(id);
            c.UpdatedAt = new DateTimeOffset(2024, 1, day, 0, 0, 0, TimeSpan.Zero);
            store.Save(c);
        }

        Assert.Equal(["c", "a", "b"], store.List().Cases.Select(c => c.Id));
    }

    [Fact]
    public void ListSkipsHiddenFilesAndIsEmptyWithoutADirectory()
    {
        using var tmp = new TempDir();
        Assert.Empty(new CaseStore(tmp.Sub("fehlt")).List().Cases);

        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        File.Copy(tmp.Sub("fall-1.db"), tmp.Sub(".halb.db"));

        Assert.Equal(["fall-1"], store.List().Cases.Select(c => c.Id));
    }

    [Fact]
    public void ListNamesTheFileThatIsNotACase()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        File.WriteAllText(tmp.Sub("kaputt.db"), "kein SQLite");
        File.WriteAllBytes(tmp.Sub("leer.db"), []);

        var (cases, unreadable) = store.List();
        Assert.Equal(["fall-1"], cases.Select(c => c.Id));
        Assert.Equal(["kaputt.db", "leer.db"], unreadable);
        Assert.Equal(0, new FileInfo(tmp.Sub("leer.db")).Length);
        Assert.Throws<CaseInvalidException>(() => store.Load("leer"));
    }

    [Fact]
    public void AFileThatIsNotADatabaseIsAnInvalidCaseNotAnUnavailableStore()
    {
        using var tmp = new TempDir();
        File.WriteAllText(tmp.Sub("fall-1.db"), "kein SQLite, aber lang genug, um wie ein Dateikopf auszusehen.......................");

        Assert.Throws<CaseInvalidException>(() => new CaseStore(tmp.Path).Load("fall-1"));
    }

    [Fact]
    public void DeleteRemovesTheCase()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        store.Save(Cases.Minimal("fall-2"));
        store.Delete("fall-1");

        Assert.Equal(["fall-2"], store.List().Cases.Select(c => c.Id));
        Assert.False(File.Exists(tmp.Sub("fall-1.db")));
        Assert.Throws<CaseNotFoundException>(() => store.Load("fall-1"));
    }

    [Fact]
    public void AMissingCaseIsNotFound()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);

        Assert.Throws<CaseNotFoundException>(() => store.Load("fall-1"));
        Assert.Throws<CaseNotFoundException>(() => store.Delete("fall-1"));
        Assert.Throws<CaseNotFoundException>(() => store.Export("fall-1"));
    }

    [Fact]
    public void AFileRenamedToAnotherIdIsRefused()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        File.Copy(tmp.Sub("fall-1.db"), tmp.Sub("fall-2.db"));

        Assert.Throws<CaseInvalidException>(() => store.Load("fall-2"));
    }

    [Theory]
    [InlineData("fall-20240101-120000-0a1b2c3d")]
    [InlineData("case.bar.2024")]
    [InlineData("A_b-c.9")]
    [InlineData("x")]
    public void AValidIdIsAccepted(string id)
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal(id));

        Assert.Equal(id, store.Load(id).Id);
    }

    [Fact]
    public void AnIdMayHaveAHundredAndTwentyEightCharactersButNoMore()
    {
        Assert.True(CaseStore.ValidId(new string('a', 128)));
        Assert.False(CaseStore.ValidId(new string('a', 129)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".hidden")]
    [InlineData("-lead")]
    [InlineData("a..b")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("fäll")]
    [InlineData("a b")]
    public void AnIdThatIsNoSafeFileNameIsRefused(string id)
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);

        Assert.False(CaseStore.ValidId(id));
        Assert.Throws<CaseInvalidException>(() => store.Save(Cases.Minimal(id)));
        Assert.Throws<CaseInvalidException>(() => store.Load(id));
        Assert.Throws<CaseInvalidException>(() => store.Delete(id));
        Assert.Empty(Directory.EnumerateFileSystemEntries(tmp.Path));
    }

    // Die ID vergibt der Dienst ("fall-…"); der Speicher nimmt keinen Fall ohne sie.
    [Fact]
    public void ACaseWithoutAnIdIsRefused()
    {
        using var tmp = new TempDir();

        Assert.Throws<CaseInvalidException>(() => new CaseStore(tmp.Path).Save(Cases.Minimal("")));
    }

    public static TheoryData<string, Action<Case>> Invalid => new()
    {
        { "blank label", c => c.Label = " " },
        { "blank taxpayer", c => c.Taxpayer.Name = "" },
        { "blank tax number", c => c.Taxpayer.TaxNumber = "\t" },
        { "blank PAB number", c => c.Taxpayer.PabNumber = "" },
        { "no period start", c => c.PeriodFrom = default },
        { "no period end", c => c.PeriodTo = default },
        { "period ends before it starts", c => c.PeriodTo = c.PeriodFrom.AddDays(-1) },
        { "unknown VAT rate", c => c.Declared = [new() { Vat = 1600, Net = 1 }] },
        { "VAT rate declared twice", c => c.Declared = [new() { Vat = 1900, Net = 1 }, new() { Vat = 1900, Net = 2 }] },
        { "negative declared revenue", c => c.Declared = [new() { Vat = 700, Net = -1 }] },
        { "product without id", c => c.Products = [new() { ProductId = "", Vat = 1900 }] },
        { "product twice", c => c.Products = [new() { ProductId = "p", Vat = 1900 }, new() { ProductId = "p", Vat = 700 }] },
        { "negative price", c => c.Products = [new() { ProductId = "p", GrossPrice = -1, Vat = 1900 }] },
        { "unknown product VAT", c => c.Products = [new() { ProductId = "p", Vat = 500 }] },
        { "empty recipe", c => c.Products = [new() { ProductId = "p", Vat = 1900, Recipe = [] }] },
        { "recipe line without ingredient", c => c.Products = [new() { ProductId = "p", Vat = 1900, Recipe = [new() { Amount = 1, Unit = "MLT" }] }] },
        { "recipe line without amount", c => c.Products = [new() { ProductId = "p", Vat = 1900, Recipe = [new() { IngredientId = "i", Unit = "MLT" }] }] },
        { "recipe line with unknown unit", c => c.Products = [new() { ProductId = "p", Vat = 1900, Recipe = [new() { IngredientId = "i", Amount = 1, Unit = "XYZ" }] }] },
        { "invoice without id", c => c.Invoices = [new() { Id = "" }] },
        { "invoice twice", c => c.Invoices = [new() { Id = "re-1" }, new() { Id = "re-1" }] },
        { "yield choice with neither target", c => c.Yields = [new() { YieldRuleId = "y" }] },
        { "yield choice with both targets", c => c.Yields = [new() { IngredientId = "i", CategoryId = "c", YieldRuleId = "y" }] },
        { "no creation time", c => c.CreatedAt = default },
        { "no update time", c => c.UpdatedAt = default },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public void AnInvalidCaseIsNotSaved(string what, Action<Case> spoil)
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1", "vorher"));
        var c = Cases.Minimal("fall-1");
        spoil(c);

        Assert.Throws<CaseInvalidException>(() => store.Save(c));
        Assert.True(store.Load("fall-1").Label == "vorher", what);
    }

    [Fact]
    public void TheBoundariesOfTheCaseRulesAreAccepted()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        var c = Cases.Minimal("fall-1");
        c.PeriodTo = c.PeriodFrom;
        c.Declared = [new() { Vat = 0, Net = 0 }, new() { Vat = 700, Net = 0 }, new() { Vat = 1900, Net = 0 }];
        c.Products = [new() { ProductId = "p", GrossPrice = 0, Vat = 0 }];
        store.Save(c);

        Cases.Same(c, store.Load("fall-1"));
    }

    [Fact]
    public void MissingListsAreSavedAsEmpty()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        var c = Cases.Minimal("fall-1");
        c.Declared = null!;
        c.Inventory = null!;
        c.Invoices = [new Invoice { Id = "re-1", Lines = null! }];
        c.Products = null!;
        c.Yields = null!;
        c.Pinned = null!;
        store.Save(c);

        var back = store.Load("fall-1");
        Assert.Empty(back.Declared);
        Assert.Empty(back.Invoices[0].Lines);
        Assert.Empty(back.Pinned);
    }

    [Fact]
    public void UnicodeInTheLabelAndTheDirectorySurvives()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Sub("Fälle – Prüfung 2024 ✓"));
        var c = Cases.Minimal("fall-1", "Café Größenwahn — „Bp“ 2024 🍺 中文");
        c.Taxpayer.Name = "Ärger & Söhne";
        store.Save(c);

        Cases.Same(c, store.Load("fall-1"));
        Assert.Equal(c.Label, Assert.Single(store.List().Cases).Label);
    }

    [Fact]
    public void AFileFromANewerSchemaIsRefusedAndLeftAsItIs()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        var path = tmp.Sub("fall-1.db");
        Sql.Exec(path, "PRAGMA user_version = 99");

        var load = Assert.Throws<CaseInvalidException>(() => store.Load("fall-1"));
        Assert.IsType<SchemaTooNewException>(load.InnerException?.InnerException ?? load.InnerException);
        Assert.Throws<CaseInvalidException>(() => store.Save(Cases.Minimal("fall-1", "überschrieben")));
        Assert.Equal(99, Sql.UserVersion(path));
        Assert.Equal("Prüfung", Sql.Scalar(path, "SELECT label FROM kase"));
    }

    [Fact]
    public void ANewFileCarriesTheCurrentSchemaVersionAndReopeningKeepsIt()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Minimal("fall-1"));
        var path = tmp.Sub("fall-1.db");
        var version = Sql.UserVersion(path);
        store.Load("fall-1");
        store.Save(Cases.Minimal("fall-1"));

        Assert.True(version > 0);
        Assert.Equal(version, Sql.UserVersion(path));
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.bak"));
    }

    [Fact]
    public void AProductWithoutItsOwnRecipeKeepsTheCatalogOne()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Full("fall-1"));

        var back = store.Load("fall-1");

        Assert.Null(back.Products.Single(p => p.ProductId == "prod.pils.05").Recipe);
        Assert.Equal(2, back.Products.Single(p => p.ProductId == "prod.radler").Recipe!.Count);
        Assert.Equal(2L, Sql.Scalar(tmp.Sub("fall-1.db"), "SELECT count(*) FROM case_product WHERE recipe_basis IS NULL"));
    }

    [Fact]
    public void AnEmptyDatabaseIsMigratedAndThenFoundToHoldNoCase()
    {
        using var tmp = new TempDir();
        Sql.Exec(tmp.Sub("fall-1.db"), "PRAGMA user_version = 0");

        Assert.Throws<CaseInvalidException>(() => new CaseStore(tmp.Path).Load("fall-1"));
    }
}

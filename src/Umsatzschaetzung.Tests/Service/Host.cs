using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rulestore;
using Umsatzschaetzung.Service;
using Umsatzschaetzung.Tagging;

namespace Umsatzschaetzung.Tests.Service;

// A LocalService over its own rule and case store. Its matcher loads the encoder on first use,
// so a host that suggests is built once per collection, not per test.
public class Host : IDisposable
{
    static readonly Tagger Tagger = new();

    // Seeding the shipped Richtsatzsammlungen costs most of a store; a copy of a seeded one is cheap.
    static readonly Lazy<string> Seeded = new(() =>
    {
        var dir = Directory.CreateTempSubdirectory("umsatzschätzung-test-seed-").FullName;
        new RuleStore(dir, TestData.Seed());
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        };
        return Path.Combine(dir, "rules.db");
    });

    readonly TempDir dir = new();

    public Host(IOcr? ocr = null, IPdfPages? pdf = null, IPdfPrinter? printer = null, Readings? readings = null)
    {
        Directory.CreateDirectory(Sub("store"));
        File.Copy(Seeded.Value, Path.Combine(Sub("store"), "rules.db"));
        Store = new RuleStore(Sub("store"), TestData.Seed());
        Cases = new CaseStore(Sub("cases"));
        Service = new LocalService(Store, Cases, ocr, Tagger, pdf, printer, "test", readings);
    }

    public RuleStore Store { get; }
    public CaseStore Cases { get; }
    public LocalService Service { get; }

    public string Sub(string name) => dir.Sub(name);

    public Task<Case> PutVorlage(string id = Vorlage.Id)
    {
        var c = Vorlage.Load();
        c.Id = id;
        return Service.PutCase(c, CancellationToken.None);
    }

    public void Dispose() => dir.Dispose();
}

public sealed class MatcherHost : Host
{
    public const string Rheinland = "Rheinland Getränke Fachgroßhandel GmbH";

    public MatcherHost()
    {
        Store.Save(new ArticleMapping
        {
            Id = "map.zwickl",
            SupplierName = Rheinland,
            SupplierArticleId = "Z-1",
            Observed = "Zwickl naturtrueb, Keg 30 l",
            IngredientId = "ing.bier.fass",
            Factor = 30000,
            Confirmed = true,
        });
    }
}

[CollectionDefinition(Name)]
public sealed class MatcherCollection : ICollectionFixture<MatcherHost>
{
    public const string Name = "matcher";
}

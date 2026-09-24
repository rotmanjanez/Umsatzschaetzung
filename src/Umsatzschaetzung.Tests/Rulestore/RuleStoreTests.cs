using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;
using Umsatzschaetzung.Rulestore;

namespace Umsatzschaetzung.Tests.Rulestore;

public class RuleStoreTests
{
    static RuleStore Open(TempDir tmp) => new(tmp.Path, TestData.Seed());

    // Nach Schlüssel geordnet, damit zwei gleiche Regelsätze gleich geschrieben werden.
    static string Dump(RuleSet rs) => Json.Serialize(new RuleSet
    {
        Version = rs.Version,
        Categories = Sorted(rs.Categories),
        Ingredients = Sorted(rs.Ingredients),
        Mappings = Sorted(rs.Mappings),
        Products = Sorted(rs.Products),
        YieldRules = Sorted(rs.YieldRules),
    });

    static Dictionary<string, T> Sorted<T>(Dictionary<string, T> d) =>
        d.OrderBy(e => e.Key, StringComparer.Ordinal).ToDictionary(e => e.Key, e => e.Value);

    static Meta Stamped(long rev = 0) => new()
    {
        ValidFrom = new DateOnly(2024, 1, 1),
        ValidTo = new DateOnly(2025, 1, 1),
        ChangedAt = new DateTimeOffset(2024, 3, 4, 5, 6, 7, TimeSpan.FromHours(1)),
        Rev = rev,
    };

    [Fact]
    public void AFreshStoreHoldsTheSeedAtVersionZero()
    {
        using var tmp = new TempDir();
        var seed = TestData.Seed();
        seed.Version = 0;

        Assert.Equal(Dump(seed), Dump(Open(tmp).Load()));
    }

    [Fact]
    public void SaveBumpsTheVersionAndStampsTheEntity()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var korn = store.Load().Products["prod.korn.4cl"];
        korn.Meta.ValidTo = new DateOnly(2024, 6, 30);

        var saved = store.Save(korn);

        Assert.Equal(1, saved.Version);
        Assert.Equal(1, korn.Meta.Rev);
        Assert.Equal(1, saved.Products["prod.korn.4cl"].Meta.Rev);
        Assert.Equal(new DateOnly(2024, 6, 30), saved.Products["prod.korn.4cl"].Meta.ValidTo);
        Assert.Equal(Dump(saved), Dump(store.Load()));
    }

    [Fact]
    public void SavesAccumulatePerEntity()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var korn = store.Load().Products["prod.korn.4cl"];
        korn.Meta.ValidTo = new DateOnly(2024, 6, 30);
        store.Save(korn);
        store.Save(new Category { Id = "cat.alkoholfrei", Name = "Alkoholfrei" });
        var merged = store.Save(new Ingredient { Id = "ing.wasser", Name = "Mineralwasser", CategoryId = "cat.alkoholfrei" });

        Assert.Equal(3, merged.Version);
        Assert.Equal((1, 2, 3), (merged.Products["prod.korn.4cl"].Meta.Rev, merged.Categories["cat.alkoholfrei"].Meta.Rev, merged.Ingredients["ing.wasser"].Meta.Rev));
        Assert.NotNull(merged.Products["prod.korn.4cl"].Meta.ValidTo);
        Assert.Equal(0, merged.Products["prod.korn.2cl"].Meta.Rev);
    }

    public static TheoryData<string> Kinds => ["category", "ingredient", "mapping", "product", "yield_rule"];

    static IRuleEntity Full(string kind) => kind switch
    {
        "category" => new Category { Id = "e", Name = "Café & Bar", Gewerbe = ["561", "56101.0"], Gebinde = ["XKG", "XBA"], Sparte = Sparte.Speisen, Meta = Stamped() },
        "ingredient" => new Ingredient { Id = "e", Name = "Gouda", CategoryId = "cat.x", Aliases = ["Schnittkäse", "Käse jung"], Piece = new(250, Unit.G), Meta = Stamped() },
        "mapping" => new ArticleMapping
        {
            Id = "e", SupplierName = "Rheinland", SupplierArticleId = "31090", Gtin = "4001234567890", Name = "Pils Fass",
            Observed = "Pils Fass 50 l", UnitCode = "XKG", IngredientId = "ing.bier.fass", Factor = 50000, Confirmed = true, Meta = Stamped(),
        },
        "product" => new Product
        {
            Id = "e", Name = "Radler", Meta = Stamped(),
            Recipe = [new() { IngredientId = "ing.b", Amount = 250, Unit = "MLT" }, new() { IngredientId = "ing.a", Amount = 250, Unit = "MLT" }, new() { ProductId = "prod.x", Amount = 2, Unit = "H87" }],
        },
        _ => new YieldRule
        {
            Id = "e", Name = "Schwund", CategoryId = "cat.x", IngredientId = "ing.y", Shrinkage = 1, OwnUse = 2, Staff = 3, Free = 4, Default = true,
            Meta = Stamped(),
        },
    };

    static IRuleEntity Sparse(string kind) => kind switch
    {
        "category" => new Category { Id = "e", Name = "n" },
        "ingredient" => new Ingredient { Id = "e", Name = "n" },
        "mapping" => new ArticleMapping { Id = "e", IngredientId = "i" },
        "product" => new Product { Id = "e", Name = "n" },
        _ => new YieldRule { Id = "e", Name = "n" },
    };

    static string One(RuleSet rs, IRuleEntity e)
    {
        var only = new RuleSet();
        only.Put(rs.Find(Kind(e), e.Id)!);
        return Json.Serialize(only);
    }

    static Entity Kind(IRuleEntity e) => e switch
    {
        Category => Entity.Category,
        Ingredient => Entity.Ingredient,
        ArticleMapping => Entity.Mapping,
        Product => Entity.Product,
        _ => Entity.YieldRule,
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EveryFieldOfAnEntityRoundTrips(string kind)
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var e = Full(kind);
        store.Save(e);
        var expected = new RuleSet();
        expected.Put(e);

        Assert.Equal(Json.Serialize(expected), One(new RuleStore(tmp.Path, TestData.Seed()).Load(), e));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void AnEntityWithOnlyItsRequiredFieldsRoundTrips(string kind)
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var e = Sparse(kind);
        store.Save(e);
        var expected = new RuleSet();
        expected.Put(e);

        Assert.Equal(Json.Serialize(expected), One(store.Load(), e));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void SavingAgainReplacesTheEntityWholly(string kind)
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        store.Save(Full(kind));
        var e = Sparse(kind);
        store.Save(e);
        var expected = new RuleSet();
        expected.Put(e);

        Assert.Equal(Json.Serialize(expected), One(store.Load(), e));
    }

    [Theory]
    [InlineData(Sparte.Unbestimmt)]
    [InlineData(Sparte.Getränke)]
    [InlineData(Sparte.Speisen)]
    [InlineData(Sparte.Handelsware)]
    public void TheSparteOfACategoryRoundTrips(Sparte sparte)
    {
        using var tmp = new TempDir();
        var store = Open(tmp);

        Assert.Equal(sparte, store.Save(new Category { Id = "c", Name = "n", Sparte = sparte }).Categories["c"].Sparte);
    }

    [Fact]
    public void DeleteDropsTheEntityAndBumpsTheVersion()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var before = store.Load();

        var pruned = store.Delete(Entity.Product, "prod.korn.2cl");

        Assert.Equal(1, pruned.Version);
        Assert.False(pruned.Products.ContainsKey("prod.korn.2cl"));
        Assert.Equal(before.Products.Count - 1, pruned.Products.Count);
        Assert.Equal(Dump(pruned), Dump(store.Load()));
    }

    [Theory]
    [InlineData(Entity.Category, "cat.bier.flasche")]
    [InlineData(Entity.Ingredient, "ing.korn")]
    [InlineData(Entity.Mapping, "map.korn07")]
    [InlineData(Entity.Product, "prod.pils.03")]
    [InlineData(Entity.YieldRule, "yr.bier.fass")]
    public void EachKindCanBeDeletedAndStaysDeletedAcrossTheSeed(Entity kind, string id)
    {
        using var tmp = new TempDir();
        Assert.NotNull(TestData.Seed().Find(kind, id));
        Open(tmp).Delete(kind, id);

        Assert.Null(Open(tmp).Load().Find(kind, id));
    }

    [Fact]
    public void ADeletedEntitySavedAgainComesBack()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var pils = store.Load().Products["prod.pils.03"];
        store.Delete(Entity.Product, "prod.pils.03");

        var back = store.Save(pils);

        Assert.Equal(2, back.Version);
        Assert.Equal(pils.Recipe.Count, back.Products["prod.pils.03"].Recipe.Count);
    }

    [Fact]
    public void TheSeedDoesNotOverwriteAnEditedEntity()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        var pils = store.Load().Products["prod.pils.03"];
        pils.Name = "Pils klein";
        store.Save(pils);

        var reopened = Open(tmp).Load();

        Assert.Equal("Pils klein", reopened.Products["prod.pils.03"].Name);
        Assert.Equal(1, reopened.Version);
    }

    [Fact]
    public void AnEntityNewInTheSeedArrivesOnTheNextStartWithoutABump()
    {
        using var tmp = new TempDir();
        Open(tmp).Save(new Category { Id = "cat.eigen", Name = "Eigen" });
        var seed = TestData.Seed();
        seed.Put(new Category { Id = "cat.neu", Name = "Neu" });

        var rs = new RuleStore(tmp.Path, seed).Load();

        Assert.True(rs.Categories.ContainsKey("cat.neu"));
        Assert.True(rs.Categories.ContainsKey("cat.eigen"));
        Assert.Equal(1, rs.Version);
    }

    [Fact]
    public void ASeedPieceWeightFillsOnlyAnIngredientWithoutOne()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        store.Save(new Ingredient { Id = "ing.gurke", Name = "Gurken" });
        store.Save(new Ingredient { Id = "ing.ei", Name = "Ei", Piece = new(55, Unit.G) });
        var seed = TestData.Seed();
        seed.Put(new Ingredient { Id = "ing.gurke", Name = "Gurke", Piece = new(400, Unit.G) });
        seed.Put(new Ingredient { Id = "ing.ei", Name = "Ei", Piece = new(60, Unit.G) });

        var rs = new RuleStore(tmp.Path, seed).Load();

        Assert.Equal((new Piece(400, Unit.G), "Gurken"), (rs.Ingredients["ing.gurke"].Piece, rs.Ingredients["ing.gurke"].Name));
        Assert.Equal(new Piece(55, Unit.G), rs.Ingredients["ing.ei"].Piece);
        Assert.Equal(2, rs.Version);
    }

    [Fact]
    public void SeedAliasesAndGebindeReachOnlyRowsNobodyEdited()
    {
        using var tmp = new TempDir();
        RuleSet Seed(string[] fass, string[] flasche, List<string> gebinde)
        {
            var seed = TestData.Seed();
            seed.Put(new Category { Id = "cat.fass", Name = "Bier vom Fass", Gebinde = gebinde });
            seed.Put(new Ingredient { Id = "ing.fass", Name = "Fassbier", CategoryId = "cat.fass", Aliases = [.. fass] });
            seed.Put(new Ingredient { Id = "ing.flasche", Name = "Flaschenbier", Aliases = [.. flasche] });
            return seed;
        }
        var store = new RuleStore(tmp.Path, Seed(["Fassbier"], ["Pils"], []));
        store.Save(new Ingredient { Id = "ing.flasche", Name = "Flaschenbier", Aliases = ["Pils", "Hausmarke"] });

        var rs = new RuleStore(tmp.Path, Seed(["Fassbier", "Pils Fass"], ["Pils", "Helles"], ["XKG"])).Load();

        Assert.Equal(["Fassbier", "Pils Fass"], rs.Ingredients["ing.fass"].Aliases);
        Assert.Equal(["Pils", "Hausmarke"], rs.Ingredients["ing.flasche"].Aliases);
        Assert.Equal(["XKG"], rs.Categories["cat.fass"].Gebinde);
        Assert.Equal(1, rs.Version);
    }

    [Fact]
    public void ReopeningRunsNoMigrationTwice()
    {
        using var tmp = new TempDir();
        Open(tmp).Save(new Category { Id = "c", Name = "n" });
        var file = tmp.Sub("rules.db");
        var schema = Sql.UserVersion(file);

        var rs = Open(tmp).Load();
        Open(tmp);

        Assert.True(schema > 0);
        Assert.Equal(schema, Sql.UserVersion(file));
        Assert.Equal(1, rs.Version);
        Assert.Equal(Dump(rs), Dump(Open(tmp).Load()));
    }

    [Fact]
    public void AStoreFromANewerProgramIsUnavailable()
    {
        using var tmp = new TempDir();
        Open(tmp);
        Sql.Exec(tmp.Sub("rules.db"), "PRAGMA user_version = 999");

        var e = Assert.Throws<StoreUnavailableException>(() => Open(tmp));
        Assert.IsType<SchemaTooNewException>(e.InnerException);
        Assert.Equal(999, Sql.UserVersion(tmp.Sub("rules.db")));
    }

    [Fact]
    public void ADirectoryThatCannotBeCreatedMakesTheStoreUnavailable()
    {
        using var tmp = new TempDir();
        File.WriteAllText(tmp.Sub("datei"), "");

        Assert.Throws<StoreUnavailableException>(() => new RuleStore(tmp.Sub("datei"), TestData.Seed()));
    }

    [Fact]
    public void TheFirstStartTakesNoSnapshotAndTheSecondOne()
    {
        using var tmp = new TempDir();
        var snapshots = tmp.Sub("snapshots");
        Open(tmp);
        Assert.Empty(Directory.GetFiles(snapshots));

        Open(tmp);

        var snapshot = Assert.Single(Directory.GetFiles(snapshots));
        Assert.Matches(@"rules-\d{8}-\d{6}-\d{3}\.db$", snapshot);
        Assert.Equal(Sql.UserVersion(tmp.Sub("rules.db")), Sql.UserVersion(snapshot));
    }

    [Fact]
    public void OnlyTheTenNewestSnapshotsAreKept()
    {
        using var tmp = new TempDir();
        var snapshots = Directory.CreateDirectory(tmp.Sub("snapshots")).FullName;
        Open(tmp);
        for (var i = 0; i < 12; i++) File.WriteAllText(Path.Combine(snapshots, $"rules-20200101-0000{i:D2}-000.db"), "");

        Open(tmp);

        var kept = Directory.GetFiles(snapshots).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(10, kept.Count);
        Assert.Equal("rules-20200101-000003-000.db", kept[0]);
        Assert.DoesNotContain("rules-20200101-000002-000.db", kept);
        Assert.StartsWith($"rules-{DateTime.Now.Year}", kept[^1]);
    }

    [Fact]
    public void AHealthyStoreHasNoNotice()
    {
        using var tmp = new TempDir();
        Open(tmp);

        Assert.Null(Open(tmp).Notice);
    }

    [Fact]
    public void ACorruptStoreIsRestoredFromTheSnapshotAndKeptAside()
    {
        using var tmp = new TempDir();
        var store = Open(tmp);
        store.Save(new Category { Id = "c1", Name = "eins" });
        store.Delete(Entity.Product, "prod.korn.2cl");
        Open(tmp);
        Open(tmp).Save(new Category { Id = "c2", Name = "nach der Sicherung" });
        File.WriteAllText(tmp.Sub("rules.db"), "kaputt");

        var restored = Open(tmp);
        var rs = restored.Load();

        Assert.NotNull(restored.Notice);
        Assert.Contains("wiederhergestellt", restored.Notice);
        Assert.Equal(2, rs.Version);
        Assert.True(rs.Categories.ContainsKey("c1"));
        Assert.False(rs.Categories.ContainsKey("c2"));
        Assert.False(rs.Products.ContainsKey("prod.korn.2cl"));
        var aside = Assert.Single(Directory.GetFiles(tmp.Path, "rules.db.defekt-*"));
        Assert.Equal("kaputt", File.ReadAllText(aside));
    }

    [Fact]
    public void ACorruptStoreWithoutSnapshotStartsAfreshFromTheSeed()
    {
        using var tmp = new TempDir();
        Open(tmp).Save(new Category { Id = "c1", Name = "eins" });
        File.WriteAllText(tmp.Sub("rules.db"), "kaputt, und nie gesichert");

        var fresh = Open(tmp);
        var seed = TestData.Seed();
        seed.Version = 0;

        Assert.Contains("neu angelegt", fresh.Notice);
        Assert.Equal(Dump(seed), Dump(fresh.Load()));
        Assert.Single(Directory.GetFiles(tmp.Path, "rules.db.defekt-*"));
    }

    [Fact]
    public void TwoStoresOpeningOneNewDirectoryAtOnceBothSeeTheWholeSeed()
    {
        using var tmp = new TempDir();
        var seed = TestData.Seed();
        var start = new Barrier(2);
        var dumps = new string[2];
        var errors = new List<Exception>();
        var threads = Enumerable.Range(0, 2).Select(i => new Thread(() =>
        {
            try
            {
                start.SignalAndWait();
                dumps[i] = Dump(new RuleStore(tmp.Path, TestData.Seed()).Load());
            }
            catch (Exception e)
            {
                lock (errors) errors.Add(e);
            }
        })).ToList();
        threads.ForEach(t => t.Start());
        threads.ForEach(t => t.Join());

        Assert.Empty(errors);
        seed.Version = 0;
        Assert.Equal(Dump(seed), dumps[0]);
        Assert.Equal(Dump(seed), dumps[1]);
    }

    [Fact]
    public void TheShippedSeedNamesEveryEntityByItsKeyAndPassesTheRuleCheck()
    {
        var seed = RuleStore.Seed();
        IEnumerable<(string Key, IRuleEntity E)> all =
        [
            .. seed.Categories.Select(e => (e.Key, (IRuleEntity)e.Value)),
            .. seed.Ingredients.Select(e => (e.Key, (IRuleEntity)e.Value)),
            .. seed.Mappings.Select(e => (e.Key, (IRuleEntity)e.Value)),
            .. seed.Products.Select(e => (e.Key, (IRuleEntity)e.Value)),
            .. seed.YieldRules.Select(e => (e.Key, (IRuleEntity)e.Value)),
        ];

        Assert.NotEmpty(all);
        Assert.All(all, x => Assert.Equal(x.Key, x.E.Id));
        Assert.All(all, x => Assert.NotEqual(default, x.E.Meta.ChangedAt));
        RuleCheck.Validate(seed);
    }

    [Fact]
    public void TheShippedSeedFillsAFreshStore()
    {
        using var tmp = new TempDir();
        var rs = new RuleStore(tmp.Path, RuleStore.Seed()).Load();
        var seed = RuleStore.Seed();
        seed.Version = 0;

        Assert.Equal(Dump(seed), Dump(rs));
    }
}

using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class RuleTests : IDisposable
{
    readonly Host host = new();
    readonly Services svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public RuleTests() => svc = host.Service;

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task SavesBumpTheVersionAndAccumulatePerEntity()
    {
        var korn = (await svc.Rules.Load(ct)).Products["prod.korn.4cl"];
        korn.Meta.ValidTo = new DateOnly(2024, 6, 30);

        var saved = await svc.Rules.Save(korn, ct);
        Assert.True(saved.Version > 0);
        Assert.Equal(saved.Version, saved.Products["prod.korn.4cl"].Meta.Rev);

        var withCategory = await svc.Rules.Save(new Category { Id = "cat.alkoholfrei", Name = "Alkoholfrei" }, ct);
        Assert.True(withCategory.Version > saved.Version);
        Assert.Contains("cat.alkoholfrei", withCategory.Categories.Keys);

        var merged = await svc.Rules.Save(new Product { Id = "prod.wasser", Unit = "H87", Name = "Mineralwasser", CategoryId = "cat.alkoholfrei" }, ct);
        Assert.True(merged.Version > withCategory.Version);
        Assert.Contains("prod.wasser", merged.Products.Keys);
        Assert.NotNull(merged.Products["prod.korn.4cl"].Meta.ValidTo);
        Assert.Equal(merged.Version, (await svc.Rules.Status(ct)).RulesVersion);
    }

    [Fact]
    public async Task AProductKeepsItsUnitWhileAMappingOrRecipeCountsInIt()
    {
        await svc.Rules.Save(new Product { Id = "prod.knolle", Name = "Knolle", Unit = "GRM" }, ct);
        await svc.Rules.Save(new Product { Id = "prod.knollenteller", Name = "Knollenteller", Unit = "H87", Recipe = [new() { PartId = "prod.knolle", Amount = 200, Unit = "GRM" }] }, ct);

        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Save(new Product { Id = "prod.knolle", Name = "Knolle", Unit = "KGM" }, ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.Contains("Knollenteller", e.Message);
        Assert.Equal("GRM", (await svc.Rules.Load(ct)).Products["prod.knolle"].Unit);

        await svc.Rules.Save(new ArticleMapping { Id = "map.knolle", Name = "Knolle lose", ProductId = "prod.knolle", Confirmed = true }, ct);
        await svc.Rules.Save(new Product { Id = "prod.knollenteller", Name = "Knollenteller", Unit = "H87" }, ct);
        var mapped = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Save(new Product { Id = "prod.knolle", Name = "Knolle", Unit = "H87" }, ct));
        Assert.Contains("Knolle lose", mapped.Message);
        Assert.Equal("GRM", (await svc.Rules.Save(new Product { Id = "prod.knollenteller", Name = "Knollenteller", Unit = "GRM" }, ct)).Products["prod.knollenteller"].Unit);
    }

    // A counted in g, B taking 200 g of it, became A in pieces and B taking 1 piece: neither goes back alone.
    [Fact]
    public async Task AnEarlierSetComesBackInOneChangeWhereNoSingleStepWould()
    {
        await svc.Rules.Save(new Product { Id = "prod.a", Name = "A", Unit = "GRM" }, ct);
        var then = await svc.Rules.Save(new Product { Id = "prod.b", Name = "B", Unit = "H87", Recipe = [new() { PartId = "prod.a", Amount = 200, Unit = "GRM" }] }, ct);
        then = Json.Copy(then);
        await svc.Rules.Change(RulesChange.Of(
            [new Product { Id = "prod.a", Name = "A", Unit = "H87" }, new Product { Id = "prod.b", Name = "B", Unit = "H87", Recipe = [new() { PartId = "prod.a", Amount = 1, Unit = "H87" }] }], []), ct);
        var now = await svc.Rules.Save(new Product { Id = "prod.c", Name = "C", Unit = "H87", Recipe = [new() { PartId = "prod.b", Amount = 1, Unit = "H87" }] }, ct);

        var back = now.Back(then);
        Assert.Equal(["prod.a", "prod.b", "prod.c"], back.Select(b => b.Id).Order(StringComparer.Ordinal));
        foreach (var (_, _, rule) in back.Where(b => b.Rule is not null))
            await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Save(rule!, ct));

        var put = back.Where(b => b.Rule is not null).Select(b => b.Rule!);
        var restored = await svc.Rules.Change(RulesChange.Of(put, back.Where(b => b.Rule is null).Select(b => (b.Kind, b.Id))), ct);
        Assert.Equal("GRM", restored.Products["prod.a"].Unit);
        Assert.Equal("GRM", restored.Products["prod.b"].Recipe[0].Unit);
        Assert.DoesNotContain("prod.c", restored.Products.Keys);
        Assert.Empty(restored.Back(then));
    }

    [Fact]
    public async Task AChangeThatLeavesTheSetInvalidWritesNothing()
    {
        var before = (await svc.Rules.Load(ct)).Version;
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Change(RulesChange.Of(
            [new Category { Id = "cat.neu", Name = "Neu" }, new Product { Id = "prod.neu", Name = "", Unit = "H87", CategoryId = "cat.neu" }], []), ct));
        Assert.Equal(ErrorCode.Invalid, e.Code);
        var rs = await svc.Rules.Load(ct);
        Assert.Equal(before, rs.Version);
        Assert.DoesNotContain("cat.neu", rs.Categories.Keys);
    }

    [Fact]
    public async Task ATemplateThatDoesNotParseIsRefused()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() =>
            svc.Rules.Save(new ReportTemplate { Id = "tpl.kaputt", Name = "Kaputt", Source = "{% if case %}offen" }, ct));

        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.Contains("endif", e.Message);
    }

    [Fact]
    public async Task ANewDefaultTemplateReplacesTheOldAndTheDefaultCannotBeDeleted()
    {
        await svc.Rules.Save(new ReportTemplate { Id = "tpl.a", Name = "A", Default = true }, ct);
        var rs = await svc.Rules.Save(new ReportTemplate { Id = "tpl.b", Name = "B", Default = true }, ct);

        Assert.Equal(["tpl.b"], rs.Templates.Values.Where(t => t.Default).Select(t => t.Id));
        var unset = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Save(new ReportTemplate { Id = "tpl.b", Name = "B" }, ct));
        Assert.Equal(ErrorCode.Conflict, unset.Code);
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Delete(Entity.Template, "tpl.b", ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.DoesNotContain("tpl.a", (await svc.Rules.Delete(Entity.Template, "tpl.a", ct)).Templates.Keys);
    }

    public static TheoryData<string, IRuleEntity> Dangling => new()
    {
        { "a product in a missing category", new Product { Id = "prod.verwaist", Unit = "H87", Name = "Kaputt", CategoryId = "cat.fehlt" } },
        { "a recipe of a missing part", new Product { Id = "prod.kaputt", Name = "Kaputt", Unit = "H87", Recipe = [new PartLine { PartId = "prod.fehlt", Amount = 1 }] } },
        { "a mapping to a missing product", new ArticleMapping { Id = "map.kaputt", Name = "Kaputt", ProductId = "prod.fehlt", Confirmed = true } },
        { "an entity without an id", new Category { Name = "Ohne" } },
    };

    [Theory]
    [MemberData(nameof(Dangling))]
    public async Task AnInvalidRuleIsRefusedAndNothingIsSaved(string what, IRuleEntity rule)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Save(rule, ct));
        Assert.True(e.Code == ErrorCode.Invalid, what + ": " + e.Code);
        Assert.IsType<RulesException>(e.InnerException);
        Assert.Equal(0, (await svc.Rules.Load(ct)).Version);
    }

    sealed class Fremd : IRuleEntity
    {
        public string Id { get; set; } = "fremd";
        public Meta Meta { get; set; } = new();
    }

    [Fact]
    public async Task AnEntityTheStoreDoesNotKnowIsAnInternalError()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Save(new Fremd(), ct));
        Assert.Equal(ErrorCode.Internal, e.Code);
        Assert.Equal(0, (await svc.Rules.Load(ct)).Version);
    }

    [Fact]
    public async Task AReferencedPartCannotBeDeleted()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Delete(Entity.Product, "prod.korn", ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.Contains("Zuordnung", e.Message);
        Assert.Contains("prod.korn", (await svc.Rules.Load(ct)).Products.Keys);
    }

    [Fact]
    public async Task AReferencedCategoryCannotBeDeleted()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Delete(Entity.Category, "cat.spirituosen", ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
    }

    [Fact]
    public async Task DeletingAnUnknownRuleIsNotFound()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Rules.Delete(Entity.Product, "prod.fehlt", ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
        Assert.Equal(0, (await svc.Rules.Load(ct)).Version);
    }

    [Fact]
    public async Task DeleteDropsTheEntityAndBumpsTheVersion()
    {
        var pruned = await svc.Rules.Delete(Entity.Product, "prod.korn.2cl", ct);
        Assert.True(pruned.Version > 0);
        Assert.DoesNotContain("prod.korn.2cl", pruned.Products.Keys);
        Assert.DoesNotContain("prod.korn.2cl", (await svc.Rules.Load(ct)).Products.Keys);
    }

    [Fact]
    public async Task ARetiredProductLeavesTheCalculation()
    {
        await host.PutVorlage();
        Assert.Contains((await svc.Reports.Calculate(Vorlage.Id, ct)).Report.Products, p => p.ProductId == "prod.korn.4cl");

        var korn = (await svc.Rules.Load(ct)).Products["prod.korn.4cl"];
        korn.Meta.ValidTo = new DateOnly(2024, 6, 30);
        await svc.Rules.Save(korn, ct);

        Assert.DoesNotContain((await svc.Reports.Calculate(Vorlage.Id, ct)).Report.Products, p => p.ProductId == "prod.korn.4cl");
    }
}

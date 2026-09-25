using Umsatzschaetzung.Model;
using Umsatzschaetzung.Rules;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class RuleTests : IDisposable
{
    readonly Host host = new();
    readonly IService svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public RuleTests() => svc = host.Service;

    public void Dispose() => host.Dispose();

    [Fact]
    public async Task SavesBumpTheVersionAndAccumulatePerEntity()
    {
        var korn = (await svc.Rules(ct)).Products["prod.korn.4cl"];
        korn.Meta.ValidTo = new DateOnly(2024, 6, 30);

        var saved = await svc.SaveRule(korn, ct);
        Assert.Equal(1, saved.Version);
        Assert.Equal(1, saved.Products["prod.korn.4cl"].Meta.Rev);

        var withCategory = await svc.SaveRule(new Category { Id = "cat.alkoholfrei", Name = "Alkoholfrei" }, ct);
        Assert.Equal(2, withCategory.Version);
        Assert.Contains("cat.alkoholfrei", withCategory.Categories.Keys);

        var merged = await svc.SaveRule(new Ingredient { Id = "ing.wasser", Name = "Mineralwasser", CategoryId = "cat.alkoholfrei" }, ct);
        Assert.Equal(3, merged.Version);
        Assert.Contains("ing.wasser", merged.Ingredients.Keys);
        Assert.NotNull(merged.Products["prod.korn.4cl"].Meta.ValidTo);
        Assert.Equal(3, (await svc.Status(ct)).RulesVersion);
    }

    [Fact]
    public async Task ATemplateThatDoesNotParseIsRefused()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() =>
            svc.SaveRule(new ReportTemplate { Id = "tpl.kaputt", Name = "Kaputt", Source = "{% if case %}offen" }, ct));

        Assert.Equal(ErrorCode.Invalid, e.Code);
        Assert.Contains("endif", e.Message);
    }

    [Fact]
    public async Task ANewDefaultTemplateReplacesTheOldAndTheDefaultCannotBeDeleted()
    {
        await svc.SaveRule(new ReportTemplate { Id = "tpl.a", Name = "A", Default = true }, ct);
        var rs = await svc.SaveRule(new ReportTemplate { Id = "tpl.b", Name = "B", Default = true }, ct);

        Assert.Equal(["tpl.b"], rs.Templates.Values.Where(t => t.Default).Select(t => t.Id));
        var unset = await Assert.ThrowsAsync<ServiceError>(() => svc.SaveRule(new ReportTemplate { Id = "tpl.b", Name = "B" }, ct));
        Assert.Equal(ErrorCode.Conflict, unset.Code);
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.DeleteRule(Entity.Template, "tpl.b", ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.DoesNotContain("tpl.a", (await svc.DeleteRule(Entity.Template, "tpl.a", ct)).Templates.Keys);
    }

    public static TheoryData<string, IRuleEntity> Dangling => new()
    {
        { "an ingredient in a missing category", new Ingredient { Id = "ing.kaputt", Name = "Kaputt", CategoryId = "cat.fehlt" } },
        { "a recipe of a missing ingredient", new Product { Id = "prod.kaputt", Name = "Kaputt", Recipe = [new RecipeLine { IngredientId = "ing.fehlt", Amount = 1 }] } },
        { "a mapping to a missing ingredient", new ArticleMapping { Id = "map.kaputt", Name = "Kaputt", IngredientId = "ing.fehlt" } },
        { "an entity without an id", new Category { Name = "Ohne" } },
    };

    [Theory]
    [MemberData(nameof(Dangling))]
    public async Task AnInvalidRuleIsRefusedAndNothingIsSaved(string what, IRuleEntity rule)
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.SaveRule(rule, ct));
        Assert.True(e.Code == ErrorCode.Invalid, what + ": " + e.Code);
        Assert.IsType<RulesException>(e.InnerException);
        Assert.Equal(0, (await svc.Rules(ct)).Version);
    }

    sealed class Fremd : IRuleEntity
    {
        public string Id { get; set; } = "fremd";
        public Meta Meta { get; set; } = new();
    }

    [Fact]
    public async Task AnEntityTheStoreDoesNotKnowIsAnInternalError()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.SaveRule(new Fremd(), ct));
        Assert.Equal(ErrorCode.Internal, e.Code);
        Assert.Equal(0, (await svc.Rules(ct)).Version);
    }

    [Fact]
    public async Task AReferencedIngredientCannotBeDeleted()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.DeleteRule(Entity.Ingredient, "ing.korn", ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
        Assert.Contains("Zuordnung", e.Message);
        Assert.Contains("ing.korn", (await svc.Rules(ct)).Ingredients.Keys);
    }

    [Fact]
    public async Task AReferencedCategoryCannotBeDeleted()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.DeleteRule(Entity.Category, "cat.spirituosen", ct));
        Assert.Equal(ErrorCode.Conflict, e.Code);
    }

    [Fact]
    public async Task DeletingAnUnknownRuleIsNotFound()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.DeleteRule(Entity.Product, "prod.fehlt", ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
        Assert.Equal(0, (await svc.Rules(ct)).Version);
    }

    [Fact]
    public async Task DeleteDropsTheEntityAndBumpsTheVersion()
    {
        var pruned = await svc.DeleteRule(Entity.Product, "prod.korn.2cl", ct);
        Assert.Equal(1, pruned.Version);
        Assert.DoesNotContain("prod.korn.2cl", pruned.Products.Keys);
        Assert.DoesNotContain("prod.korn.2cl", (await svc.Rules(ct)).Products.Keys);
    }

    [Fact]
    public async Task ARetiredProductLeavesTheCalculation()
    {
        await host.PutVorlage();
        Assert.Contains((await svc.Calculate(Vorlage.Id, ct)).Report.Products, p => p.ProductId == "prod.korn.4cl");

        var korn = (await svc.Rules(ct)).Products["prod.korn.4cl"];
        korn.Meta.ValidTo = new DateOnly(2024, 6, 30);
        await svc.SaveRule(korn, ct);

        Assert.DoesNotContain((await svc.Calculate(Vorlage.Id, ct)).Report.Products, p => p.ProductId == "prod.korn.4cl");
    }
}

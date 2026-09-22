using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class RulesTests
{
    [Theory]
    [InlineData("Pils 0,5 l", "pils 0,5 l")]
    [InlineData("  PILS   0,5l!! ", "pils 0,5l")]
    [InlineData("Coca-Cola / Zero", "coca cola zero")]
    [InlineData("Größe: XL", "größe xl")]
    [InlineData("---", "")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void ACanonicalNameIsLowerCaseWordsSeparatedBySingleSpaces(string? name, string canonical) =>
        Assert.Equal(canonical, ArticleName.Canonical(name));

    [Theory]
    [InlineData(null, true)]
    [InlineData("2023-12-31", false)]
    [InlineData("2024-01-01", true)]
    [InlineData("2024-12-31", true)]
    [InlineData("2025-01-01", false)]
    public void MetaIsValidFromItsFirstDayUpToButNotIncludingItsEnd(string? date, bool valid)
    {
        var m = new Meta { ValidFrom = new DateOnly(2024, 1, 1), ValidTo = new DateOnly(2025, 1, 1) };
        Assert.Equal(valid, m.ValidOn(date is null ? null : DateOnly.Parse(date)));
    }

    [Fact]
    public void AnOpenMetaIsAlwaysValid() => Assert.True(new Meta().ValidOn(new DateOnly(1900, 1, 1)));

    [Fact]
    public void RuleSetPutAndFindEveryEntity()
    {
        var rs = new RuleSet();
        IRuleEntity[] all =
        [
            new Category { Id = "c" }, new Ingredient { Id = "i" }, new ArticleMapping { Id = "m" },
            new Product { Id = "p" }, new YieldRule { Id = "y" },
        ];
        foreach (var e in all) rs.Put(e);
        Assert.Same(all[0], rs.Find(Entity.Category, "c"));
        Assert.Same(all[1], rs.Find(Entity.Ingredient, "i"));
        Assert.Same(all[2], rs.Find(Entity.Mapping, "m"));
        Assert.Same(all[3], rs.Find(Entity.Product, "p"));
        Assert.Same(all[4], rs.Find(Entity.YieldRule, "y"));
        Assert.Null(rs.Find(Entity.Product, "c"));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("56101.0", true)]
    [InlineData("47250.0", true)]
    [InlineData("47710.0", false)]
    public void ACategoryCoversTheGewerbeItNamesByPrefix(string? kennzahl, bool covers) =>
        Assert.Equal(covers, new Category { Gewerbe = ["561", "47250.0"] }.Covers(kennzahl));

    [Fact]
    public void ACategoryWithoutGewerbeCoversEveryOne() => Assert.True(new Category().Covers("47710.0"));
}

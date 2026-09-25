using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class SuppliersTests
{
    static Invoice Scan(string supplier, params string[] lines) => new()
    {
        Source = Source.Scan,
        SupplierName = supplier,
        Lines = [.. lines.Select(n => new InvoiceLine { Name = n, UnitCode = "C62" })],
    };

    static Case Of(params Invoice[] invoices)
    {
        for (var i = 0; i < invoices.Length; i++) invoices[i].Id = "re-" + i;
        return new() { Invoices = [.. invoices] };
    }

    static bool Unify(Case c) => Suppliers.Unify(c, c.Invoices.Select(i => i.Id).ToHashSet());

    static string[] Names(Case c) => [.. c.Invoices.Select(i => i.SupplierName)];

    [Fact]
    public void PartsAndSpellingsOfOneSupplierTakeItsFullName()
    {
        var c = Of(
            Scan("Sommer", "2022 Domina trocken 0,75 l"),
            Scan("WEIN GUT", "2022 Domina trocken 0,75 l"),
            Scan("Weingut Sommer", "2022 Domina trocken 0,75 l", "Silvaner 1 l"),
            Scan("Weingut Sommer", "Silvaner 1 l"),
            Scan("WEINGUT SOMMER", "Silvaner 1 l"));
        Assert.True(Unify(c));
        Assert.All(Names(c), n => Assert.Equal("Weingut Sommer", n));
    }

    [Fact]
    public void AMisreadLetterJoinsWhenTheArticlesAgree()
    {
        var c = Of(Scan("Weingut Sommer", "Silvaner 1 l"), Scan("Weinqut Sommer", "Silvaner 1 l"), Scan("Weingut Sommer", "Riesling 1 l"));
        Assert.True(Unify(c));
        Assert.All(Names(c), n => Assert.Equal("Weingut Sommer", n));
    }

    [Fact]
    public void ANamePartWithoutACommonArticleStays()
    {
        var c = Of(Scan("Sommer", "Brötchen"), Scan("Weingut Sommer", "Silvaner 1 l"));
        Assert.False(Unify(c));
        Assert.Equal(["Sommer", "Weingut Sommer"], Names(c));
    }

    [Fact]
    public void APartJoinsTheSupplierItSharesMostWith()
    {
        var c = Of(
            Scan("Weingut", "Silvaner 1 l", "Domina 0,75 l"),
            Scan("Weingut Sommer", "Silvaner 1 l", "Domina 0,75 l"),
            Scan("Weingut Huber", "Silvaner 1 l"));
        Unify(c);
        Assert.Equal(["Weingut Sommer", "Weingut Sommer", "Weingut Huber"], Names(c));
    }

    [Fact]
    public void AnEInvoiceStatesTheName()
    {
        var c = Of(
            Scan("Weingut Sommer", "Silvaner 1 l"),
            Scan("Weingut Sommer", "Silvaner 1 l"),
            new Invoice { Source = Source.Cii, SupplierName = "Weingut Sommer GbR", Lines = [new() { Name = "Silvaner 1 l" }] });
        Unify(c);
        Assert.All(Names(c), n => Assert.Equal("Weingut Sommer GbR", n));
    }

    [Fact]
    public void AnEmptyNameIsLeftToTheReader()
    {
        var c = Of(Scan("", "Silvaner 1 l"), Scan("Weingut Sommer", "Silvaner 1 l"));
        Assert.False(Unify(c));
        Assert.Equal("", c.Invoices[0].SupplierName);
    }

    [Fact]
    public void ANameAlreadyInTheCaseStaysAndIsTakenOver()
    {
        var c = Of(Scan("Weingut Sommer & Söhne", "Silvaner 1 l"), Scan("Weingut Sommer", "Silvaner 1 l"), Scan("WEINGUT SOMMER", "Silvaner 1 l"));
        Assert.True(Suppliers.Unify(c, new HashSet<string> { "re-1", "re-2" }));
        Assert.Equal(["Weingut Sommer & Söhne", "Weingut Sommer & Söhne", "Weingut Sommer & Söhne"], Names(c));
    }

    [Fact]
    public void OnlyTheFreshInvoicesAreRenamed()
    {
        var c = Of(Scan("Sommer", "Silvaner 1 l"), Scan("Weingut Sommer", "Silvaner 1 l"));
        Assert.False(Suppliers.Unify(c, new HashSet<string>()));
        Assert.Equal(["Sommer", "Weingut Sommer"], Names(c));
    }
}

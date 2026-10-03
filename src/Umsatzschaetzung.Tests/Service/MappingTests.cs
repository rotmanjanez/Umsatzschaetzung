using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

[Collection(MatcherCollection.Name)]
public class MappingTests(MatcherHost host)
{
    const string Rheinland = MatcherHost.Rheinland;
    static readonly InvoiceLine Korn = new() { Name = "Doppelkorn 38 % vol, Flasche 0,7 l", UnitCode = "XBO" };

    readonly Services svc = host.Service;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task AFriseurIsNeverOfferedKorn()
    {
        var friseur = await svc.Cases.Put(Vorlage.Blank("Salon", "96021.0"), ct);
        var sugs = await svc.Mapping.Suggest(friseur.Id, Korn, null, ct);
        Assert.DoesNotContain(sugs, s => s.Mapping.ProductId == "prod.korn");
    }

    [Fact]
    public async Task ACaseWithoutGewerbeSeesEverything()
    {
        var neu = await svc.Cases.Put(Vorlage.Blank(), ct);
        Assert.Equal("prod.korn", (await svc.Mapping.Suggest(neu.Id, Korn, null, ct))[0].Mapping.ProductId);
    }

    [Fact]
    public async Task AnEmptyCaseIdSuggestsWithoutFilter()
    {
        var sugs = await svc.Mapping.Suggest("", Korn, null, ct);
        Assert.Equal("prod.korn", sugs[0].Mapping.ProductId);
        Assert.Equal(700, sugs[0].Mapping.Factor);
        Assert.Equal(OriginKind.Encoder, sugs[0].Kind);
        Assert.Equal("", sugs[0].Mapping.Id);
    }

    [Fact]
    public async Task AnUnknownCaseSuggestsWithoutFilter()
    {
        var sugs = await svc.Mapping.Suggest("fall-gibt-es-nicht", Korn, null, ct);
        Assert.Equal("prod.korn", sugs[0].Mapping.ProductId);
    }

    [Fact]
    public async Task AnExactHitLeadsAndTheEncodersAlternativesFollow()
    {
        var line = new InvoiceLine { Name = Korn.Name, SellerArticleId = "Z-1", UnitCode = "XBO" };
        var sugs = await svc.Mapping.Suggest("", line, Rheinland, ct);
        Assert.True(sugs.Count > 1);
        Assert.Equal((OriginKind.Exact, "map.zwickl"), (sugs[0].Kind, sugs[0].Mapping.Id));
        Assert.Equal((OriginKind.Encoder, "prod.korn"), (sugs[1].Kind, sugs[1].Mapping.ProductId));
        Assert.DoesNotContain(sugs.Skip(1), s => s.Mapping.ProductId == "prod.bier.fass");
    }

    // The rules are shared by whoever asks; an exact hit edited before it is saved must not change them.
    [Fact]
    public async Task AnExactHitIsTheCallersToEdit()
    {
        var line = new InvoiceLine { Name = Korn.Name, SellerArticleId = "Z-1", UnitCode = "XBO" };
        var hit = (await svc.Mapping.Suggest("", line, Rheinland, ct))[0].Mapping;
        var factor = hit.Factor;
        hit.Factor = (factor ?? 0) + 1;
        hit.Meta = new Meta();

        var again = (await svc.Mapping.Suggest("", line, Rheinland, ct))[0].Mapping;
        Assert.Equal(factor, again.Factor);
        Assert.NotEqual(0, again.Meta.Rev);
    }

    static Invoice Zwickl(string articleId) => new()
    {
        Source = Source.Ubl, SupplierName = Rheinland, Number = "R-Z", Date = new DateOnly(2024, 3, 1),
        Lines = [new InvoiceLine { No = 1, Name = "Zwickl naturtrueb, Keg 50 l", SellerArticleId = articleId, UnitCode = "XKG", Quantity = 1000, MappingId = "map.zwickl" }],
    };

    [Fact]
    public async Task VerifyKeepsARuleThatStillFitsTheLine()
    {
        var v = await svc.Invoices.Verify(new VerifyReq("", Zwickl("Z-1"), Intent.Check, null, null), ct);
        Assert.Equal("map.zwickl", v.Invoice.Lines[0].MappingId);
    }

    [Fact]
    public async Task VerifyLeavesARuleBehindOnceTheArticleNumberIsEdited()
    {
        var v = await svc.Invoices.Verify(new VerifyReq("", Zwickl("Z-9"), Intent.Check, null, null), ct);
        Assert.True(string.IsNullOrEmpty(v.Invoice.Lines[0].MappingId));
    }

    [Fact]
    public async Task MapCaseMapsWhatTheMatcherIsSureAboutIntoTheCase()
    {
        var kase = Vorlage.Blank("Nachzügler", "56101.0");
        kase.Invoices =
        [
            new Invoice
            {
                Id = "re-late", Source = Source.Ubl, SupplierName = Rheinland, Number = "R-L", Date = new DateOnly(2024, 4, 1),
                Lines =
                [
                    new InvoiceLine { No = 1, Name = "Zwickl naturtrueb, Keg 50 l", SellerArticleId = "Z-1", UnitCode = "XKG", Quantity = 1000 },
                    new InvoiceLine { No = 2, Name = "Fassbier Pils, Keg 50 l", UnitCode = "XKG", Quantity = 2000 },
                ],
            },
        ];
        var late = await svc.Cases.Put(kase, ct);

        var caughtUp = await svc.Mapping.Map(late.Id, ct);

        Assert.Equal("map.zwickl", caughtUp.Invoices[0].Lines[0].MappingId);
        var guessed = caughtUp.Invoices[0].Lines[1].MappingId;
        Assert.False(string.IsNullOrEmpty(guessed));
        Assert.Equal(7, Guid.Parse(guessed).Version);
        Assert.False((await svc.Rules.Load(ct)).Mappings.ContainsKey(guessed));
        var saved = await svc.Cases.Get(late.Id, ct);
        Assert.Equal(["map.zwickl", guessed], saved.Invoices[0].Lines.Select(l => l.MappingId));
        Assert.Equal((false, "prod.bier.fass"), saved.Mappings[guessed!] is var m ? (m.Confirmed, m.ProductId) : default);
    }

    [Fact]
    public async Task MapCaseLeavesAnUnsureLineOpenAndTheCaseUntouched()
    {
        var kase = Vorlage.Blank("Unklar");
        kase.Invoices =
        [
            new Invoice
            {
                Id = "re-unklar", Source = Source.Ubl, SupplierName = "Irgendwer", Number = "U-1", Date = new DateOnly(2024, 4, 1),
                Lines = [new InvoiceLine { No = 1, Name = "Servietten 3-lagig 250 Stk", UnitCode = "H87", Quantity = 1000 }],
            },
        ];
        var stored = await svc.Cases.Put(kase, ct);

        var after = await svc.Mapping.Map(stored.Id, ct);

        Assert.Null(after.Invoices[0].Lines[0].MappingId);
        Assert.Equal(stored.UpdatedAt, (await svc.Cases.Get(stored.Id, ct)).UpdatedAt);
    }

    [Fact]
    public async Task MapCaseAsksOncePerRulesVersion()
    {
        var kase = Vorlage.Blank("Einmal");
        kase.Invoices =
        [
            new Invoice
            {
                Id = "re-einmal", Source = Source.Ubl, SupplierName = "Irgendwer", Number = "E-1", Date = new DateOnly(2024, 4, 1),
                Lines = [new InvoiceLine { No = 1, Name = "Servietten 3-lagig 250 Stk", UnitCode = "H87", Quantity = 1000 }],
            },
        ];
        var stored = await svc.Cases.Put(kase, ct);
        var version = (await svc.Rules.Load(ct)).Version;

        var first = await svc.Mapping.Map(stored.Id, ct);
        var again = await svc.Mapping.Map(stored.Id, ct);

        Assert.Equal(version, first.MappedAt);
        Assert.Equal(version, again.MappedAt);
        Assert.Equal(version, (await svc.Cases.Get(stored.Id, ct)).MappedAt);
    }

    [Fact]
    public async Task MapCaseOfAnUnknownCaseIsNotFound()
    {
        var e = await Assert.ThrowsAsync<ServiceError>(() => svc.Mapping.Map("fall-gibt-es-nicht", ct));
        Assert.Equal(ErrorCode.NotFound, e.Code);
    }
}

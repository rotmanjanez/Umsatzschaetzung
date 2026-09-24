using System.Text.Json;
using System.Text.Json.Nodes;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Model;

public class JsonTests
{
    static Invoice Sample() => new()
    {
        Id = "re-1",
        Source = Source.Zugferd,
        FileName = "Rechnung „Bräu“.pdf",
        SupplierName = "Brauerei Bräu & Söhne <GmbH>",
        Number = "RE-20201121/508",
        Date = new DateOnly(2020, 11, 21),
        Currency = "EUR",
        NetTotal = 49_600,
        GrossTotal = 57_104,
        StatedGross = 57_105,
        Verification = new Verification { At = new DateTimeOffset(2025, 1, 8, 14, 5, 0, TimeSpan.FromHours(1)), Auto = true },
        Lines =
        [
            new() { No = 1, Name = "Pils", SellerArticleId = "A-17", Gtin = "4006381333931", Quantity = 20_500, UnitCode = "XBO",
                UnitPrice = 12_345_679, PriceBaseQty = 10_000, LineNet = 2_531, Vat = 1900, MappingId = "map.pils" },
            new() { No = 2, Name = "Pfand", Quantity = -1_000, UnitCode = "H87", LineNet = -800 },
        ],
    };

    [Fact]
    public void APieceWeightIsWrittenAsInTheSeedAndLeftOutWhenUnknown()
    {
        const string json = """{"ingredients":{"ing.gurke":{"name":"Gurken","piece":{"amount":400,"unit":"g"}},"ing.salz":{"name":"Salz"}}}""";
        var rs = Json.Deserialize<RuleSet>(json);
        Assert.Equal(new Piece(400, Unit.G), rs.Ingredients["ing.gurke"].Piece);
        Assert.Null(rs.Ingredients["ing.salz"].Piece);
        var back = JsonNode.Parse(Json.Serialize(rs))!["ingredients"]!;
        Assert.Equal("""{"amount":400,"unit":"g"}""", back["ing.gurke"]!["piece"]!.ToJsonString());
        Assert.Null(back["ing.salz"]!.AsObject()["piece"]);
        Assert.False(back["ing.salz"]!.AsObject().ContainsKey("piece"));
    }

    [Fact]
    public void AnInvoiceSurvivesARoundTrip()
    {
        var json = Json.Serialize(Sample());
        Assert.Equal(json, Json.Serialize(Json.Deserialize<Invoice>(json)));
        var back = Json.Deserialize<Invoice>(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Equal((Source.Zugferd, "RE-20201121/508", (DateOnly?)new DateOnly(2020, 11, 21), (long?)57_105, (long?)null),
            (back.Source, back.Number, back.Date, back.StatedGross, back.StatedNet));
        Assert.Equal(new DateTimeOffset(2025, 1, 8, 13, 5, 0, TimeSpan.Zero), back.Verification!.At);
        Assert.Equal(-800, back.Lines[1].LineNet);
    }

    [Fact]
    public void AnInvoiceIsWrittenInCamelCaseWithEnumNamesAndIsoDates()
    {
        var o = JsonNode.Parse(Json.Serialize(Sample()))!.AsObject();
        Assert.Equal("zugferd", (string?)o["source"]);
        Assert.Equal("2020-11-21", (string?)o["date"]);
        Assert.Equal(57_105, (long?)o["statedGross"]);
        Assert.Equal(12_345_679, (long?)o["lines"]![0]!["unitPrice"]);
        Assert.Equal(true, (bool?)o["verification"]!["auto"]);
    }

    [Fact]
    public void NullsAreLeftOutAndDefaultsAreKept()
    {
        var o = JsonNode.Parse(Json.Serialize(new Invoice()))!.AsObject();
        Assert.False(o.ContainsKey("date"));
        Assert.False(o.ContainsKey("statedNet"));
        Assert.False(o.ContainsKey("verification"));
        Assert.Equal("ubl", (string?)o["source"]);
        Assert.Equal(0, (long?)o["netTotal"]);
        Assert.Equal("", (string?)o["number"]);
        var line = JsonNode.Parse(Json.Serialize(Sample()))!["lines"]![1]!.AsObject();
        Assert.False(line.ContainsKey("gtin"));
        Assert.False(line.ContainsKey("mappingId"));
    }

    [Fact]
    public void UmlautsAndQuotesAreWrittenAsTheyAre()
    {
        var json = Json.Serialize(Sample());
        Assert.Contains("\"Brauerei Bräu & Söhne <GmbH>\"", json);
        Assert.Contains("„Bräu“", json);
    }

    [Fact]
    public void UnknownFieldsAreIgnoredAndMissingOnesKeepTheirDefault()
    {
        var inv = Json.Deserialize<Invoice>("""{"number":"R1","vatBreakdown":[{"vat":700}],"lines":[{"no":1,"expectedFactor":10000}]}""");
        Assert.Equal("R1", inv.Number);
        Assert.Equal(Source.Ubl, inv.Source);
        Assert.Null(inv.Date);
        var l = Assert.Single(inv.Lines);
        Assert.Equal((1L, "", 0L), (l.No, l.Name, l.PriceBaseQty));
    }

    [Theory]
    [InlineData("""{"source":"pdf"}""")]
    [InlineData("""{"date":"08.01.2025"}""")]
    [InlineData("""{"netTotal":"12.34"}""")]
    [InlineData("""{"netTotal":12.34}""")]
    [InlineData("{")]
    public void MalformedValuesAreRejected(string json) => Assert.ThrowsAny<JsonException>(() => Json.Deserialize<Invoice>(json));

    [Fact]
    public void ANullDocumentIsRejected()
    {
        var e = Assert.Throws<JsonException>(() => Json.Deserialize<Invoice>("null"));
        Assert.Equal("leeres Dokument", e.Message);
        Assert.Throws<JsonException>(() => Json.Deserialize<RuleSet>("null"u8));
    }

    [Fact]
    public void TheFixtureRuleSetSurvivesARoundTrip()
    {
        var rs = TestData.Seed();
        var json = Json.Serialize(rs);
        var back = Json.Deserialize<RuleSet>(json);
        Assert.Equal(json, Json.Serialize(back));
        Assert.Equal(1041, back.Version);
        Assert.Equal(rs.Mappings.Keys.Order(), back.Mappings.Keys.Order());
        Assert.Equal(50_000, back.Mappings["map.fass50"].Factor);
        Assert.Equal(new DateOnly(2024, 1, 1), back.Ingredients["ing.korn"].Meta.ValidFrom);
    }

    [Fact]
    public void RuleEnumsUseTheirSnakeCaseNames()
    {
        var rs = new RuleSet();
        rs.Put(new Category { Id = "c.g", Sparte = Sparte.Getränke });
        rs.Put(new Category { Id = "c.u" });
        var o = JsonNode.Parse(Json.Serialize(rs))!["categories"]!;
        Assert.Equal("getraenke", (string?)o["c.g"]!["sparte"]);
        Assert.False(o["c.u"]!.AsObject().ContainsKey("sparte"));
        Assert.Equal(Sparte.Handelsware, Json.Deserialize<RuleSet>("""{"categories":{"c":{"sparte":"handelsware"}}}""").Categories["c"].Sparte);
    }

    [Fact]
    public void DefaultFlagsAndRevisionsAreLeftOut()
    {
        var rs = new RuleSet();
        rs.Put(new YieldRule { Id = "y.a" });
        rs.Put(new YieldRule { Id = "y.b", Default = true, Meta = new() { Rev = 3 } });
        var o = JsonNode.Parse(Json.Serialize(rs))!["yieldRules"]!;
        Assert.False(o["y.a"]!.AsObject().ContainsKey("default"));
        Assert.False(o["y.a"]!["meta"]!.AsObject().ContainsKey("rev"));
        Assert.Equal(true, (bool?)o["y.b"]!["default"]);
        Assert.Equal(3, (long?)o["y.b"]!["meta"]!["rev"]);
    }

    [Fact]
    public void AnEmptyRuleSetReadsFromAnEmptyObject()
    {
        var rs = Json.Deserialize<RuleSet>("{}");
        Assert.Empty(rs.Categories);
        Assert.Empty(rs.Mappings);
        Assert.Equal(0, rs.Version);
    }

    [Fact]
    public void ACopyOfACaseHoldsEverythingAndSharesNothing()
    {
        var c = Umsatzschaetzung.Tests.Casefile.Cases.Full("fall-1");
        c.MappedAt = 7;
        var copy = Json.Copy(c);
        Assert.Equal(Json.Serialize(c), Json.Serialize(copy));
        copy.Invoices[1].Lines[0].Name = "anders";
        copy.Declared.Clear();
        Assert.NotEqual("anders", c.Invoices[1].Lines[0].Name);
        Assert.NotEmpty(c.Declared);
    }

    [Fact]
    public void ARuleEntityIsCopiedAsItsOwnKind()
    {
        IRuleEntity rule = new Ingredient { Id = "ing.gurke", Name = "Gurken", Piece = new Piece(400, Unit.G) };
        var copy = Json.Copy(rule);
        var ingredient = Assert.IsType<Ingredient>(copy);
        Assert.NotSame(rule, copy);
        Assert.Equal(("ing.gurke", "Gurken", new Piece(400, Unit.G)), (ingredient.Id, ingredient.Name, ingredient.Piece));
    }
}

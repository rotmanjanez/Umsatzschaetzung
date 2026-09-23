using System.Text.Json.Nodes;
using Umsatzschaetzung.Reports;

namespace Umsatzschaetzung.Tests.Reports;

public class TemplateTests
{
    static readonly JsonObject Data = JsonNode.Parse("""
        {
          "titel": "Bier & <Brot>",
          "zitat": "er sagte \"ja\"\nund 'nein'\\",
          "zeilen": ["a", "b"],
          "leer": [],
          "leeresObjekt": {},
          "betrag": 733595,
          "negativ": -123456,
          "satz": 40456,
          "menge": 20000,
          "basis": 5000,
          "code": "LTR",
          "roh": "<b>",
          "lage": "über",
          "wahr": true,
          "falsch": false,
          "null": 0,
          "text": "",
          "bruch": 1.5,
          "nichts": null,
          "tag": "2024-03-15",
          "zeit": "2024-03-15T12:00:00+00:00",
          "sparte": "getraenke",
          "einheit": "ml",
          "schluessel": "x",
          "tief": { "x": { "y": "unten" } },
          "karte": { "p": 1, "q": 2 }
        }
        """)!.AsObject();

    static string Render(string source) => Template.Render(source, Data);

    [Fact]
    public void PlainTextPassesThrough() => Assert.Equal("<p>{ nur } Text %}</p>", Render("<p>{ nur } Text %}</p>"));

    [Fact]
    public void AValueIsEscaped() => Assert.Equal("<h1>Bier &amp; &lt;Brot&gt;</h1>", Render("<h1>{{ titel }}</h1>"));

    [Fact]
    public void QuotesAreEscaped() => Assert.Equal("er sagte &quot;ja&quot;\nund &#39;nein&#39;\\", Render("{{ zitat }}"));

    [Fact]
    public void CssWritesAQuotedCssString()
    {
        Assert.Equal("\"Bier & \\3c Brot\\3e \"", Render("{{ titel | css }}"));
        Assert.Equal("\"er sagte \\\"ja\\\"\\A und 'nein'\\\\\"", Render("{{ zitat | css }}"));
    }

    [Theory]
    [InlineData("{{ wahr }}", "true")]
    [InlineData("{{ null }}", "0")]
    [InlineData("{{ bruch }}", "1.5")]
    [InlineData("{{ nichts }}", "")]
    [InlineData("{{titel}}", "Bier &amp; &lt;Brot&gt;")]
    public void ScalarsArePrintedAsTheyAre(string source, string expected) => Assert.Equal(expected, Render(source));

    [Theory]
    [InlineData("{{ betrag | cents }}", "7.335,95 €")]
    [InlineData("{{ negativ | cents }}", "-1.234,56 €")]
    [InlineData("{{ satz | bp }}", "404,56 %")]
    [InlineData("{{ betrag | portions }}", "733.595 Portionen")]
    [InlineData("{{ negativ | group }}", "-123.456")]
    [InlineData("{{ menge | milli }}", "20")]
    [InlineData("{{ negativ | micro }}", "-0,123456")]
    [InlineData("{{ tag | date }}", "15.03.2024")]
    [InlineData("{{ zeit | day }}", "15.03.2024")]
    [InlineData("{{ sparte | sparte }}", "Getränke")]
    [InlineData("{{ code | sparte }}", "Übrige")]
    [InlineData("{{ code | unitname }}", "Liter")]
    [InlineData("{{ menge | qty:einheit }}", "20 l")]
    [InlineData("{{ menge | quantity:code }}", "20 Liter")]
    [InlineData("{{ betrag | price:basis:code }}", "0,733595 € je 5 Liter")]
    public void EveryFilterFormatsItsRawValue(string source, string expected) => Assert.Equal(expected, Render(source));

    [Fact]
    public void FiltersChain() => Assert.Equal("\"7.335,95 €\"", Render("{{ betrag | cents | css }}"));

    [Fact]
    public void AListIsRepeated() =>
        Assert.Equal("<li>a</li>\n<li>b</li>\n", Render("{% for z in zeilen %}<li>{{ z }}</li>\n{% endfor %}"));

    [Fact]
    public void AnObjectIsRepeatedOverItsValues() => Assert.Equal("12", Render("{% for v in karte %}{{ v }}{% endfor %}"));

    [Fact]
    public void AnInnerLoopVariableShadowsTheOuter() =>
        Assert.Equal("a:ab|b:ab|", Render("{% for z in zeilen %}{{ z }}:{% for z in zeilen %}{{ z }}{% endfor %}|{% endfor %}"));

    [Fact]
    public void ALoopVariableShadowsTheData() => Assert.Equal("ab", Render("{% for titel in zeilen %}{{ titel }}{% endfor %}"));

    [Fact]
    public void ALoopVariableEndsWithItsLoop() => Assert.Throws<TemplateError>(() => Render("{% for z in zeilen %}{% endfor %}{{ z }}"));

    [Theory]
    [InlineData("wahr", "ja")]
    [InlineData("falsch", "nein")]
    [InlineData("null", "nein")]
    [InlineData("bruch", "ja")]
    [InlineData("text", "nein")]
    [InlineData("titel", "ja")]
    [InlineData("leer", "nein")]
    [InlineData("zeilen", "ja")]
    [InlineData("leeresObjekt", "nein")]
    [InlineData("karte", "ja")]
    [InlineData("nichts", "nein")]
    public void AConditionTestsTruth(string path, string expected)
    {
        Assert.Equal(expected, Render($"{{% if {path} %}}ja{{% else %}}nein{{% endif %}}"));
        Assert.Equal(expected == "ja" ? "" : "nicht", Render($"{{% if not {path} %}}nicht{{% endif %}}"));
    }

    [Theory]
    [InlineData("über", "ja")]
    [InlineData("unter", "nein")]
    [InlineData("Über", "nein")]
    public void AConditionComparesAgainstALiteral(string literal, string expected) =>
        Assert.Equal(expected, Render($"{{% if lage == \"{literal}\" %}}ja{{% else %}}nein{{% endif %}}"));

    [Fact]
    public void ANumberComparesByItsText() => Assert.Equal("ja", Render("{% if satz == \"40456\" %}ja{% endif %}"));

    [Fact]
    public void ConditionsNest() =>
        Assert.Equal("2", Render("{% if wahr %}{% if falsch %}1{% else %}2{% endif %}{% else %}3{% endif %}"));

    [Theory]
    [InlineData("{{ tief.x.y }}")]
    [InlineData("{{ tief[schluessel].y }}")]
    public void NestedPathsResolve(string source) => Assert.Equal("unten", Render(source));

    [Theory]
    [InlineData("{{ fehlt }}")]
    [InlineData("{{ tief.fehlt }}")]
    [InlineData("{{ titel.x }}")]
    [InlineData("{{ tief[fehlt] }}")]
    [InlineData("{{ tief[schluessel }}")]
    [InlineData("{{ fehlt | cents }}")]
    [InlineData("{% if fehlt %}{% endif %}")]
    [InlineData("{% for x in fehlt %}{% endfor %}")]
    public void AnUnknownPathIsAnError(string source) => Assert.Throws<TemplateError>(() => Render(source));

    [Theory]
    [InlineData("{{ betrag | kilo }}")]
    [InlineData("{{ betrag | cents | kilo }}")]
    public void AnUnknownFilterIsAnError(string source) => Assert.Throws<TemplateError>(() => Render(source));

    [Theory]
    [InlineData("{{ titel | cents }}")]
    [InlineData("{{ wahr | bp }}")]
    [InlineData("{{ titel | css | cents }}")]
    [InlineData("{{ tief | css }}")]
    [InlineData("{{ zeilen }}")]
    [InlineData("{{ menge | qty:code }}")]
    [InlineData("{% for x in titel %}{% endfor %}")]
    public void AValueOfTheWrongKindIsAnError(string source) => Assert.Throws<TemplateError>(() => Render(source));

    [Theory]
    [InlineData("{{ titel")]
    [InlineData("{% if wahr %}offen")]
    [InlineData("{% if wahr %}{% else %}offen")]
    [InlineData("{% for z in zeilen %}offen")]
    [InlineData("{% for z zeilen %}{% endfor %}")]
    [InlineData("{% if lage == über %}{% endif %}")]
    [InlineData("{% if a b c %}{% endif %}")]
    [InlineData("{% while wahr %}{% endwhile %}")]
    public void AMalformedTemplateIsAnError(string source) => Assert.Throws<TemplateError>(() => Render(source));

    [Fact]
    public void ABlockTagOnItsOwnLineTakesItsLineWithIt() =>
        Assert.Equal("<ul>\n  <li>a</li>\n  <li>b</li>\n</ul>\n", Render("""
            <ul>
              {% for z in zeilen %}
              <li>{{ z }}</li>
              {% endfor %}
            </ul>

            """));

    [Fact]
    public void AnElseOnItsOwnLineTakesItsLineWithIt() =>
        Assert.Equal("  nein\n", Render("  {% if falsch %}\n  ja\n  {% else %}\n  nein\n  {% endif %}\n"));

    [Fact]
    public void TextBeforeATagOnTheSameLineIsKept() => Assert.Equal("a: ja", Render("a: {% if wahr %}ja{% endif %}"));

    [Fact]
    public void TheSpaceBetweenAValueAndATagIsKept() => Assert.Equal("7.335,95 € ja", Render("{{ betrag | cents }} {% if wahr %}ja{% endif %}"));

    [Fact]
    public void AnInlineTagKeepsTheLineBreakAfterIt() => Assert.Equal("a ja\nb", Render("a {% if wahr %}ja{% endif %}\nb"));

    [Fact]
    public void AFilteredValueIsEscaped() => Assert.Equal("5 &lt;b&gt;", Render("{{ basis | quantity:roh }}"));

    [Fact]
    public void ACssStringCannotCloseTheStyleSheet()
    {
        var data = new JsonObject { ["label"] = "</style><script>" };
        Assert.DoesNotContain("<", Template.Render("{{ label | css }}", data));
    }
}

using Umsatzschaetzung.Calc;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Richtsatz;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Reports;

public class HtmlTests
{
    static readonly Case Kase = Vorlage.Load();
    static readonly RuleSet Rules = TestData.Seed();
    static readonly Report Report = Calculation.Run(Kase, Rules);

    static Sammlung Sammlung(int year) =>
        Json.Deserialize<Sammlung>(File.ReadAllBytes(Path.Combine(TestData.Repo, "data", "richtsatz", year + ".json")));

    [Fact]
    public void TheGewerbekennzahlFindsItsRahmensatz()
    {
        var rahmen = Vergleich.Aufschlag(Sammlung(2023), "56101.0", 12_000_000);
        Assert.NotNull(rahmen);
        Assert.Equal((178, 400), (rahmen.Von, rahmen.Bis));
        Assert.StartsWith("Gast-", rahmen.Klasse);
        Assert.Null(Vergleich.Aufschlag(Sammlung(2023), "561", 12_000_000));
    }

    [Fact]
    public void TheReportMeasuresTheMarkupAgainstTheRahmensatz()
    {
        var rahmen = Vergleich.Aufschlag(Sammlung(2023), "56101.0", 12_000_000);
        Assert.Contains(
            "Richtsatzsammlung 2023, „Gast-, Speise- und Schankwirtschaften“: Rohaufschlag 178 bis 400 % (Mittel 257 %), kalkuliert über dem Rahmen.",
            Html.Render(Kase, Rules, Report, rahmen));
    }

    [Theory]
    [InlineData(300, 500, "im Rahmen")]
    [InlineData(500, 900, "unter dem Rahmen")]
    public void TheReportSaysWhereTheMarkupLies(int von, int bis, string lage) =>
        Assert.Contains($"Rohaufschlag {von} bis {bis} % (Mittel {von} %), kalkuliert {lage}.",
            Html.Render(Kase, Rules, Report, new Rahmen(2024, "Klasse", null, new Satz(von, bis, von))));

    [Fact]
    public void WithoutRahmenThereIsNoComparison() =>
        Assert.DoesNotContain("kalkuliert über dem Rahmen", Html.Render(Kase, Rules, Report, null));

    [Fact]
    public void TheReportCarriesTheCalculation()
    {
        var html = Html.Render(Kase, Rules, Report, null);
        Assert.StartsWith("<!DOCTYPE html>", html.TrimStart(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<h1>2 Rohgewinnaufschlag</h1>", html);
        Assert.Contains("<h1>3 Umsatz über den Rohgewinnaufschlagsatz</h1>", html);
        Assert.Contains("<th class=\"wide\">Getränke</th>", html);
        Assert.Contains("Anhang D", html);
        Assert.Contains("2024-04711", html);
        Assert.Contains("Pils 0,3 l vom Fass", html);
        Assert.DoesNotContain("{{", html);
        Assert.DoesNotContain("{%", html);
    }

    [Fact]
    public void OutsideGastronomyTheCalculationIsOneTable()
    {
        var kase = Vorlage.Load();
        kase.Taxpayer.Gewerbe = "47241.0";

        var html = Html.Render(kase, Rules, Calculation.Run(kase, Rules), null);

        Assert.Contains("<th class=\"wide\">Produkt</th>", html);
        Assert.DoesNotContain("<th class=\"wide\">Speisen</th>", html);
    }

    [Fact]
    public void WhatTheCaseSaysIsEscaped()
    {
        var kase = Vorlage.Load();
        kase.Label = "Bar <script>";
        kase.Taxpayer.Name = "Müller & \"Söhne\"";

        var html = Html.Render(kase, Rules, Calculation.Run(kase, Rules), null);

        Assert.Contains("Bar &lt;script&gt;", html);
        Assert.Contains("Müller &amp; &quot;Söhne&quot;", html);
        Assert.Contains("\"Müller & \\\"Söhne\\\"\"", html);
    }
}

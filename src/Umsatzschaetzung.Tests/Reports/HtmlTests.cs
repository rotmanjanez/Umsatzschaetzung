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
            "Richtsatzsammlung 2023, „Gast-, Speise- und Schankwirtschaften“: Rohgewinnaufschlagsatz 178 bis 400 % (Mittel 257 %), kalkuliert über dem Rahmen.",
            Html.Render(Kase, Rules, Report, rahmen));
    }

    [Theory]
    [InlineData(300, 500, "im Rahmen")]
    [InlineData(500, 900, "unter dem Rahmen")]
    public void TheReportSaysWhereTheMarkupLies(int von, int bis, string lage) =>
        Assert.Contains($"Rohgewinnaufschlagsatz {von} bis {bis} % (Mittel {von} %), kalkuliert {lage}.",
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
    }

    [Fact]
    public void WhatTheCaseSaysIsNeverReadAsTemplate()
    {
        var kase = Vorlage.Load();
        kase.Label = "{{ case.taxpayer.taxNumber }}";
        kase.Taxpayer.Name = "{% for r in rules %}console.log(){% endfor %}";

        var html = Html.Render(kase, Rules, Calculation.Run(kase, Rules), null);

        Assert.Contains("{{ case.taxpayer.taxNumber }}", html);
        Assert.Contains("{% for r in rules %}console.log(){% endfor %}", html);
    }

    [Fact]
    public void AScriptInTheCaseStaysText()
    {
        var kase = Vorlage.Load();
        kase.Taxpayer.Name = "<script src=\"http://example.com/x.js\"></script><img src=x onerror=alert(1)>";

        var html = Html.Render(kase, Rules, Calculation.Run(kase, Rules), null);

        Assert.Contains("&lt;script src=&quot;http://example.com/x.js&quot;&gt;&lt;/script&gt;&lt;img src=x onerror=alert(1)&gt;", html);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
    }

    static RuleSet WithTemplates(params ReportTemplate[] templates)
    {
        var rs = TestData.Seed();
        foreach (var t in templates) rs.Put(t);
        return rs;
    }

    [Fact]
    public void TheCaseChoosesItsTemplateAndFallsBackToTheDefault()
    {
        var rs = WithTemplates(
            new ReportTemplate { Id = "tpl.a", Name = "A", Source = "A {{ template.id }}", Default = true },
            new ReportTemplate { Id = "tpl.b", Name = "B", Source = "B {{ template.name }}" });
        var kase = Vorlage.Load();

        Assert.Equal("A tpl.a", Html.Render(kase, rs, Report, null));
        kase.TemplateId = "tpl.b";
        Assert.Equal("B B", Html.Render(kase, rs, Report, null));
        kase.TemplateId = "tpl.gelöscht";
        Assert.Equal("A tpl.a", Html.Render(kase, rs, Report, null));
    }

    [Fact]
    public void RulesWithoutTemplatesRenderTheShippedReport() =>
        Assert.Contains("Umsätze vor und nach Betriebsprüfung", Html.Render(Kase, Rules, Report, null));

    [Fact]
    public void TheTemplateSeesMoreThanTheShippedReportUses()
    {
        var rs = WithTemplates(new ReportTemplate
        {
            Id = "tpl.x", Name = "X", Default = true,
            Source = "{{ periodDays }}|{{ appVersion }}|{{ marks.topRight }}|{{ richtsatz.jahr }}|{{ richtsatz.klasse.staffeln[0].sätze.rohgewinnI.mittel }}"
                + "|{% for s in suppliers %}{{ s.name }}:{{ s.invoices }};{% endfor %}",
        });
        var kase = Vorlage.Load();
        kase.Taxpayer.Gewerbe = "56101.0";
        var sammlung = Sammlung(2023);
        var info = new SammlungInfo(2023, sammlung.Klassen.Count, "rs-2023.pdf", DateTimeOffset.UnixEpoch);

        var parts = Html.Render(kase, rs, Report, null, sammlung, info, "1.2.3").Split('|');

        Assert.Equal((kase.PeriodTo.DayNumber - kase.PeriodFrom.DayNumber + 1).ToString(), parts[0]);
        Assert.Equal(("1.2.3", Format.Period(kase.PeriodFrom, kase.PeriodTo), "2023", "72"), (parts[1], parts[2], parts[3], parts[4]));
        Assert.Equal(string.Concat(kase.Invoices.GroupBy(i => i.SupplierName).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key}:{g.Count()};")), parts[5]);
    }
}

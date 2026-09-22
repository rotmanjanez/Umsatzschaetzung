using System.Text;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Reports;
using Umsatzschaetzung.Tests.Service;

namespace Umsatzschaetzung.Tests.Reports;

public class CsvTests
{
    static readonly Case Kase = Vorlage.Load();
    static readonly RuleSet Rules = TestData.Seed();

    static string Text(byte[] csv) => Encoding.UTF8.GetString(csv);

    static string[] Rows(Case c, Invoice inv) => Text(Csv.Invoice(c, inv, Rules)).TrimStart('\uFEFF').Split("\r\n");

    [Fact]
    public void TheFileStartsWithAByteOrderMark() =>
        Assert.Equal([0xEF, 0xBB, 0xBF], Csv.Invoice(Kase, Kase.Invoices[0], Rules)[..3]);

    [Fact]
    public void TheHeaderDescribesTheInvoice()
    {
        var rows = Rows(Kase, Kase.Invoices[0]);
        Assert.Equal(
        [
            "Prüfung;Schankwirtschaft Zum Alten Fass, Bp 2024",
            "Datei;rheinland-2024-04711.pdf",
            "Quelle;" + Format.Source(Source.Zugferd),
            "Lieferant;Rheinland Getränke Fachgroßhandel GmbH",
            "Rechnungsnummer;2024-04711",
            "Datum;15.03.2024",
            "Netto;1.690,00 €",
            "Brutto;2.011,10 €",
            "Geprüft;",
            "",
            "Zeile;Position;Artikelnummer;GTIN;Menge;Einzelpreis;Netto;USt;Zuordnung",
        ], rows[..11]);
    }

    [Fact]
    public void EveryLineCarriesItsMapping()
    {
        var rows = Rows(Kase, Kase.Invoices[0]);
        Assert.Equal("1;Pils Fass 50 l;31090;;12 Keg;92,50 €;1.110,00 €;19 %;Fassbier Pils × 50 l", rows[11]);
        Assert.Equal(15, rows.Length);
        Assert.Equal("", rows[^1]);
    }

    [Fact]
    public void AVerifiedInvoiceSaysSo()
    {
        var inv = Vorlage.Load().Invoices[0];
        var at = new DateTimeOffset(2024, 5, 2, 8, 0, 0, TimeSpan.Zero);
        inv.Verification = new Verification { At = at, Auto = true };
        Assert.Contains("Geprüft;" + Format.Verified(at, true), Rows(Kase, inv));
    }

    [Theory]
    [InlineData("Kiste; 20 x 0,5 l", "\"Kiste; 20 x 0,5 l\"")]
    [InlineData("Zoll \"Brett\"", "\"Zoll \"\"Brett\"\"\"")]
    [InlineData("zwei\nZeilen", "\"zwei\nZeilen\"")]
    [InlineData(" eingerückt", "\" eingerückt\"")]
    [InlineData("\\.", "\"\\.\"")]
    [InlineData("schlicht", "schlicht")]
    public void ACellIsQuotedWhereItWouldBreakTheRow(string name, string cell)
    {
        var inv = new Invoice { Lines = [new InvoiceLine { No = 1, Name = name }] };
        Assert.StartsWith("1;" + cell + ";", Text(Csv.Invoice(Kase, inv, Rules)).Split("\r\n", 12)[11]);
    }

    [Fact]
    public void AnUnmappedLineSaysSo()
    {
        var inv = new Invoice { Lines = [new InvoiceLine { No = 7, Name = "Servietten", Quantity = 1000, UnitCode = "H87", LineNet = 500, Vat = 1900 }] };
        Assert.Equal("7;Servietten;;;1 Stück;0,00 €;5,00 €;19 %;ungeklärt", Rows(Kase, inv)[11]);
    }
}

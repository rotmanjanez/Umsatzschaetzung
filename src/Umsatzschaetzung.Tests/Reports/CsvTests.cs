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

public class AssortmentCsvTests
{
    static readonly RuleSet Rules = TestData.Seed();
    static readonly Product First = Rules.Products.Values.OrderBy(p => p.Id, StringComparer.Ordinal).First();
    static readonly Product Second = Rules.Products.Values.OrderBy(p => p.Id, StringComparer.Ordinal).Skip(1).First();

    static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    [Fact]
    public void TheListedProductsRoundTrip()
    {
        var kase = new Case
        {
            Products =
            [
                new CaseProduct { ProductId = First.Id, GrossPrice = 123450, Vat = 1900 },
                new CaseProduct { ProductId = Second.Id, Vat = 700 },
            ],
        };

        var csv = Csv.Assortment(kase, Rules);
        var text = Encoding.UTF8.GetString(csv).TrimStart('﻿');
        Assert.StartsWith("Produkt;Bruttopreis;USt;Produkt-ID\r\n", text);
        Assert.Contains("1.234,50 €;19 %;" + First.Id + "\r\n", text);

        var read = Csv.ReadAssortment(csv, Rules);
        Assert.Empty(read.Unknown);
        Assert.Equivalent(kase.Products.OrderBy(p => p.ProductId), read.Products.OrderBy(p => p.ProductId), strict: true);
    }

    [Fact]
    public void AHandwrittenFileMatchesByNameInAnyCase()
    {
        var read = Csv.ReadAssortment(Bytes($"produkt,bruttopreis\n{First.Name.ToUpperInvariant()},\"4.50\"\nUnbekannt,1\n\n"), Rules);

        var p = Assert.Single(read.Products);
        Assert.Equal((First.Id, 450L, 1900L), (p.ProductId, p.GrossPrice, p.Vat));
        Assert.Equal(["Unbekannt"], read.Unknown);
    }

    [Theory]
    [InlineData("12,5", 1250)]
    [InlineData("1.234", 123400)]
    [InlineData("1.234,56 €", 123456)]
    [InlineData("1,234.56", 123456)]
    [InlineData("", 0)]
    public void APriceReadsInGermanAndEnglishNotation(string price, long cents)
    {
        var read = Csv.ReadAssortment(Bytes($"Produkt-ID;Bruttopreis\r\n{First.Id};{price}\r\n"), Rules);
        Assert.Equal(cents, Assert.Single(read.Products).GrossPrice);
    }

    [Fact]
    public void ALatin1FileStillMatchesUmlauts()
    {
        var rules = TestData.Seed();
        rules.Products[First.Id].Name = "Käsespätzle";
        var read = Csv.ReadAssortment(Encoding.Latin1.GetBytes("Produkt;USt\nKäsespätzle;7 %\n"), rules);
        Assert.Equal((First.Id, 700L), (Assert.Single(read.Products).ProductId, read.Products[0].Vat));
    }

    [Fact]
    public void AnUnreadablePriceNamesItsLine() =>
        Assert.Contains("Zeile 3", Assert.Throws<InvalidDataException>(() =>
            Csv.ReadAssortment(Bytes($"Produkt-ID;Bruttopreis\n{First.Id};1\n{Second.Id};teuer\n"), Rules)).Message);

    [Fact]
    public void AFileWithoutAProductColumnIsRejected() =>
        Assert.Throws<InvalidDataException>(() => Csv.ReadAssortment(Bytes("Name;Preis\nBier;3\n"), Rules));
}

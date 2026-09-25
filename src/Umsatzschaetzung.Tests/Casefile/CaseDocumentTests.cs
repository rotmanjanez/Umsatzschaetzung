using Umsatzschaetzung.Casefile;
using Umsatzschaetzung.Model;

namespace Umsatzschaetzung.Tests.Casefile;

public class CaseDocumentTests
{
    static CaseStore Store(TempDir tmp)
    {
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Full("fall-1"));
        return store;
    }

    [Fact]
    public void AStoredDocumentLoadsBackUnderItsFileName()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        store.SaveFile("fall-1", "re-1", "Rechnung März.pdf", [0, 255, 7]);

        Cases.HoldsFile(store, "fall-1", "re-1", "Rechnung März.pdf", [0, 255, 7]);
    }

    [Fact]
    public void TheDocumentKeepsOnlyItsFileNameNotTheFolder()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        store.SaveFile("fall-1", "re-1", Path.Combine("irgendwo", "tief", "beleg.xml"), [1]);

        Assert.Equal("beleg.xml", store.LoadFile("fall-1", "re-1").Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData(".versteckt")]
    [InlineData("ordner/")]
    public void ADocumentWithoutAUsableFileNameIsRefused(string name)
    {
        using var tmp = new TempDir();
        var store = Store(tmp);

        Assert.Throws<CaseInvalidException>(() => store.SaveFile("fall-1", "re-1", name, [1]));
    }

    [Fact]
    public void ADocumentReplacesTheEarlierOneOfItsInvoice()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        store.SaveFile("fall-1", "re-1", "a.pdf", [1]);
        store.SaveFile("fall-1", "re-1", "b.pdf", [2, 2]);
        store.SaveFile("fall-1", "re-2", "c.jpg", [3]);

        Cases.HoldsFile(store, "fall-1", "re-1", "b.pdf", [2, 2]);
        Cases.HoldsFile(store, "fall-1", "re-2", "c.jpg", [3]);
    }

    [Fact]
    public void SavingTheCaseKeepsItsDocuments()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        store.SaveFile("fall-1", "re-1", "a.pdf", [1]);
        store.Save(Cases.Full("fall-1"));

        Assert.Equal("a.pdf", store.LoadFile("fall-1", "re-1").Name);
    }

    [Fact]
    public void ACaseSavedWithAnAttachmentHoldsItsDocumentAndReading()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        store.Save(Cases.Full("fall-1"), new Attachment("re-1", "a.pdf", [1, 2], [Page()]));

        Cases.HoldsFile(store, "fall-1", "re-1", "a.pdf", [1, 2]);
        Assert.Single(store.LoadReading("fall-1", "re-1")!);
    }

    [Fact]
    public void ACaseThatFailsToSaveLeavesNoDocumentBehind()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        var c = Cases.Full("fall-1");
        c.Label = "";

        Assert.Throws<CaseInvalidException>(() => store.Save(c, new Attachment("re-9", "a.pdf", [1], [Page()])));
        Assert.Throws<CaseNotFoundException>(() => store.LoadFile("fall-1", "re-9"));
        Assert.Null(store.LoadReading("fall-1", "re-9"));
    }

    [Fact]
    public void ADeletedInvoiceKeepsItsDocumentUntilThePurge()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        store.SaveFile("fall-1", "re-1", "a.pdf", [1]);
        store.SaveReading("fall-1", "re-1", [Page()]);
        store.SaveFile("fall-1", "re-2", "b.pdf", [2]);
        store.SaveReading("fall-1", "re-2", [Page()]);
        var c = Cases.Full("fall-1");
        c.Invoices.RemoveAll(i => i.Id == "re-1");
        store.Save(c);

        Assert.Equal("a.pdf", store.LoadFile("fall-1", "re-1").Name);

        store.Purge();

        Assert.Throws<CaseNotFoundException>(() => store.LoadFile("fall-1", "re-1"));
        Assert.Null(store.LoadReading("fall-1", "re-1"));
        Assert.Equal("b.pdf", store.LoadFile("fall-1", "re-2").Name);
        Assert.NotNull(store.LoadReading("fall-1", "re-2"));
    }

    [Fact]
    public void APurgeSkipsAFileThatIsNoCase()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        File.WriteAllText(tmp.Sub("kaputt.db"), "keine Datenbank");
        store.SaveFile("fall-1", "re-9", "a.pdf", [1]);

        store.Purge();

        Assert.Throws<CaseNotFoundException>(() => store.LoadFile("fall-1", "re-9"));
        Assert.Equal("keine Datenbank", File.ReadAllText(tmp.Sub("kaputt.db")));
    }

    [Fact]
    public void APurgeOfAMissingFolderDoesNothing()
    {
        using var tmp = new TempDir();
        new CaseStore(tmp.Sub("fehlt")).Purge();

        Assert.False(Directory.Exists(tmp.Sub("fehlt")));
    }

    [Fact]
    public void ADocumentNeedsAnExistingCase()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);

        Assert.Throws<CaseNotFoundException>(() => store.SaveFile("fall-1", "re-1", "a.pdf", [1]));
        Assert.Throws<CaseNotFoundException>(() => store.SaveReading("fall-1", "re-1", [Page()]));
        Assert.Throws<CaseNotFoundException>(() => store.LoadFile("fall-1", "re-1"));
        Assert.Null(store.LoadReading("fall-1", "re-1"));
        Assert.False(File.Exists(tmp.Sub("fall-1.db")));
    }

    [Fact]
    public void AnInvoiceWithoutADocumentHasNone()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);

        Assert.Throws<CaseNotFoundException>(() => store.LoadFile("fall-1", "re-1"));
        Assert.Null(store.LoadReading("fall-1", "re-1"));
    }

    [Theory]
    [InlineData("../re-1")]
    [InlineData("")]
    [InlineData("re 1")]
    public void AnInvoiceIdThatIsNoSafeKeyIsRefused(string invoiceId)
    {
        using var tmp = new TempDir();
        var store = Store(tmp);

        Assert.Throws<CaseInvalidException>(() => store.SaveFile("fall-1", invoiceId, "a.pdf", [1]));
        Assert.Throws<CaseInvalidException>(() => store.LoadFile("fall-1", invoiceId));
        Assert.Throws<CaseInvalidException>(() => store.LoadReading("fall-1", invoiceId));
        var c = Cases.Full("fall-1");
        c.Invoices[0].Id = invoiceId;
        Assert.Throws<CaseInvalidException>(() => store.Save(c));
    }

    [Fact]
    public void AReadingRoundTripsWithItsWordsHeaderLinesCellsAndFlags()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        List<OcrPage> pages = [Page(), new OcrPage { Width = 10, Height = 20 }, Page()];
        store.SaveReading("fall-1", "re-1", pages);

        var back = store.LoadReading("fall-1", "re-1")!;

        Assert.Equal(pages.Select(Text), back.Select(Text));
        Assert.All(back, p => Assert.Null(p.Image));
    }

    [Fact]
    public void ANewReadingReplacesTheOldOneAndLeavesTheDocument()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        store.SaveFile("fall-1", "re-1", "a.pdf", [1]);
        store.SaveReading("fall-1", "re-1", [Page(), Page()]);
        store.SaveReading("fall-1", "re-1", [new OcrPage { Width = 1, Height = 2 }]);

        var back = Assert.Single(store.LoadReading("fall-1", "re-1")!);
        Assert.Equal((1, 2), (back.Width, back.Height));
        Assert.Empty(back.Words);
        Assert.Equal("a.pdf", store.LoadFile("fall-1", "re-1").Name);
    }

    static OcrPage Page() => new()
    {
        Image = new Raster(1, 1, [1, 2, 3, 4]),
        Width = 1240,
        Height = 1754,
        Correction = new Correction { Scale = 0.75, Skew = -2.5, Turn = 180, Settle = 0.25 },
        Words = [Word("Rechnung", 0.5f), Word("Nr.", 1f), Word("ä€", 0f)],
        Header = { [Field.InvoiceNumber] = Word("R-1", 0.93f), [Field.Supplier] = Word("Rheinland", 0.25f) },
        Flags = [new Flag { Code = "sum", Message = "Summe weicht ab", Field = Field.NetTotal }, new Flag { Code = "x", Message = "", LineNo = 3 }],
        Lines =
        [
            new OcrLine
            {
                Parsed = new InvoiceLine { No = 1, Name = "Pils", SellerArticleId = "31090", Quantity = 12000, UnitCode = "XKG",
                    UnitPrice = 92500000, PriceBaseQty = 1000, LineNet = 111000, Vat = 1900, MappingId = "map.fass50" },
                Cells = { [Field.Quantity] = Word("12", 0.8f), [Field.Name] = Word("Pils", 0.7f) },
                Flags = [new Flag { Code = "no_unit", Message = "ohne Einheit", LineNo = 1, Field = Field.Unit }],
            },
            new OcrLine(),
        ],
    };

    static OcrWord Word(string text, float confidence) =>
        new() { Text = text, Box = new Box(text.Length, 2, 30, 40), Confidence = confidence };

    // Ein Abbild der Lesung ohne Seitenbild; Wörterbücher nach Feld sortiert.
    static string Text(OcrPage p)
    {
        static string W(OcrWord w) => $"{w.Text}@{w.Box}:{w.Confidence}";
        static string F(Flag f) => $"{f.Code}|{f.Message}|{f.LineNo}|{f.Field}";
        static string D(Dictionary<Field, OcrWord> d) => string.Join(",", d.OrderBy(e => e.Key).Select(e => $"{e.Key}={W(e.Value)}"));
        var c = p.Correction;
        return $"{p.Width}x{p.Height} {c.Scale}/{c.Skew}/{c.Turn}/{c.Settle} [{string.Join(",", p.Words.Select(W))}] {{{D(p.Header)}}} "
            + $"<{string.Join(",", p.Flags.Select(F))}> "
            + string.Join(";", p.Lines.Select(l => $"{Json.Serialize(new Invoice { Lines = [l.Parsed] })} {{{D(l.Cells)}}} <{string.Join(",", l.Flags.Select(F))}>"));
    }
}

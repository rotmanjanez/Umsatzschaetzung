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
    public void APurgeDropsTheMappingsNoLineUsesAnyMore()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        var c = Cases.Full("fall-1");
        c.Mappings["map-alt"] = new() { Id = "map-alt", IngredientId = "ing.korn" };
        store.Save(c);

        store.Purge();

        Assert.Equal(["map-pils"], store.Load("fall-1").Mappings.Keys);
    }

    [Fact]
    public void APurgedDocumentLeavesNoBytesInTheFile()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        var scan = System.Text.Encoding.ASCII.GetBytes("GEHEIMER-SCAN-" + new string('x', 8192));
        store.SaveFile("fall-1", "re-1", "a.pdf", scan);
        var c = Cases.Full("fall-1");
        c.Invoices.RemoveAll(i => i.Id == "re-1");
        store.Save(c);

        store.Purge();

        Assert.Equal(-1, File.ReadAllBytes(tmp.Sub(CaseStore.FileName(c.Label))).AsSpan().IndexOf(scan.AsSpan(0, 32)));
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

    [Theory]
    [InlineData("re-2", true)]
    [InlineData("re-1", false)]
    [InlineData("re-3", false)]
    [InlineData("re-3", true)]
    public void AnInvoiceSavedAloneLeavesTheCaseAsAFullSaveWould(string invoiceId, bool unmapped)
    {
        using var full = new TempDir();
        using var alone = new TempDir();
        var before = Cases.Full("fall-1");
        before.MappedStore = "regeln";
        var expected = new CaseStore(full.Path);
        var store = new CaseStore(alone.Path);
        expected.Save(before);
        store.Save(before);
        expected.SaveFile("fall-1", "re-2", "alt.jpg", [9]);
        store.SaveFile("fall-1", "re-2", "alt.jpg", [9]);
        var at = new DateTimeOffset(2025, 2, 1, 10, 0, 0, TimeSpan.FromHours(1));
        var made = new Dictionary<string, ArticleMapping>
        {
            ["map-neu"] = new() { Id = "map-neu", SupplierName = "Rheinland", Name = "Weizen", IngredientId = "ing.weizen", Factor = 500 },
            ["map-pils"] = new() { Id = "map-pils", Name = "Pils neu", IngredientId = "ing.bier.fass" },
        };
        var add = new Attachment(invoiceId, "neu.pdf", [4, 5, 6], [Page(), Page()], [[7], [8, 8]]);

        var c = expected.Load("fall-1");
        foreach (var (id, m) in made) c.Mappings[id] = m;
        var i = c.Invoices.FindIndex(x => x.Id == invoiceId);
        if (i >= 0) c.Invoices[i] = Invoice(invoiceId, unmapped);
        else c.Invoices.Add(Invoice(invoiceId, unmapped));
        if (unmapped) c.MappedStore = null;
        c.UpdatedAt = at;
        expected.Save(c, add);

        var kept = store.SaveInvoice("fall-1", Invoice(invoiceId, unmapped), made, add, at);

        var want = expected.Load("fall-1");
        var got = store.Load("fall-1");
        Cases.Same(want, got);
        Assert.Equal(unmapped ? null : "regeln", kept);
        Assert.Equal((want.MappedStore, want.MappedAt), (got.MappedStore, got.MappedAt));
        foreach (var id in (string[])["re-1", "re-2", "re-3"])
        {
            Assert.Equal(expected.LoadReading("fall-1", id)?.Select(Text), store.LoadReading("fall-1", id)?.Select(Text));
            Assert.Equal(expected.LoadImages("fall-1", id), store.LoadImages("fall-1", id));
        }
        Cases.HoldsFile(store, "fall-1", invoiceId, "neu.pdf", [4, 5, 6]);
        if (invoiceId != "re-2") Cases.HoldsFile(store, "fall-1", "re-2", "alt.jpg", [9]);
    }

    [Fact]
    public void AnInvoiceSavedAloneKeepsItsPlaceOrComesLast()
    {
        using var tmp = new TempDir();
        var store = Store(tmp);
        var at = DateTimeOffset.UnixEpoch;

        store.SaveInvoice("fall-1", Invoice("re-3", false), [], new Attachment("re-3", "", [], null), at);
        store.SaveInvoice("fall-1", Invoice("re-2", false), [], new Attachment("re-2", "", [], null), at);

        Assert.Equal(["re-2", "re-1", "re-3"], store.Load("fall-1").Invoices.Select(i => i.Id));
    }

    [Fact]
    public void AnInvoiceSavedAloneNeedsAnExistingCaseAndASafeId()
    {
        using var tmp = new TempDir();
        var store = new CaseStore(tmp.Path);
        var add = new Attachment("re-1", "a.pdf", [1], null);

        Assert.Throws<CaseNotFoundException>(() => store.SaveInvoice("fall-1", Invoice("re-1", false), [], add, DateTimeOffset.UnixEpoch));
        Assert.False(File.Exists(tmp.Sub("fall-1.db")));

        Store(tmp);
        Assert.Throws<CaseInvalidException>(() =>
            store.SaveInvoice("fall-1", Invoice("../re-1", false), [], add with { InvoiceId = "../re-1" }, DateTimeOffset.UnixEpoch));
        Cases.Same(Cases.Full("fall-1"), store.Load("fall-1"));
    }

    static Invoice Invoice(string id, bool unmapped) => new()
    {
        Id = id, Source = Source.Scan, FileName = "neu.pdf", SupplierName = "Rheinland", Number = "N-" + id,
        Date = new DateOnly(2024, 7, 1), Currency = "EUR", NetTotal = 500, GrossTotal = 595,
        Verification = new() { At = new DateTimeOffset(2025, 2, 1, 9, 0, 0, TimeSpan.Zero), Auto = false },
        Lines =
        [
            new() { No = 1, Name = "Weizen", Quantity = 1000, UnitCode = "XBO", UnitPrice = 500, PriceBaseQty = 1, LineNet = 500, Vat = 1900,
                MappingId = "map-neu" },
            new() { No = 2, Name = "Pils", Quantity = 1000, UnitCode = "XBO", UnitPrice = 0, PriceBaseQty = 1, LineNet = 0, Vat = 1900,
                MappingId = unmapped ? null : "map-pils" },
        ],
    };

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
                    UnitPrice = 92500000, PriceBaseQty = 1000, LineNet = 111000, Vat = 1900 },
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

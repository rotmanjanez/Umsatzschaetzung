using System.Runtime.CompilerServices;
using SkiaSharp;
using Umsatzschaetzung.Model;
using Umsatzschaetzung.Service;

namespace Umsatzschaetzung.Tests.Service;

public sealed class SourceTests : IDisposable
{
    sealed class Pages : IPdfPages
    {
        public readonly List<int> Dpis = [];

        public Task<SKBitmap> Page(byte[] pdf, int index, int dpi, CancellationToken ct)
        {
            Dpis.Add(dpi);
            return Task.FromResult(Tests.Scan.Sheets.Blank(index + 1, 1));
        }

        public async IAsyncEnumerable<SKBitmap> Rasterize(byte[] pdf, int dpi, [EnumeratorCancellation] CancellationToken ct)
        {
            Dpis.Add(dpi);
            for (var i = 1; i <= 2; i++)
            {
                await Task.Yield();
                yield return Tests.Scan.Sheets.Blank(i, 1);
            }
        }
    }

    readonly Pages pages = new();
    readonly Host host;
    readonly IService svc;
    readonly CancellationToken ct = TestContext.Current.CancellationToken;

    public SourceTests()
    {
        host = new Host(pdf: pages);
        svc = host.Service;
    }

    public void Dispose() => host.Dispose();

    async Task<string> Store(string fileName, byte[] data, List<OcrPage>? reading = null)
    {
        var kase = await svc.PutCase(Vorlage.Blank(), ct);
        var v = await svc.VerifyInvoice(new VerifyReq(kase.Id, new Invoice { Number = "1" }, Intent.Store, fileName, data, reading), ct);
        return v.Case!.Id + "/" + v.Invoice.Id;
    }

    [Fact]
    public async Task APdfIsShownPageByPage()
    {
        var at = (await Store("zugferd.pdf", File.ReadAllBytes(TestData.File("zugferd.pdf")))).Split('/');

        var source = await svc.InvoiceSource(at[0], at[1], ct);

        Assert.Equal("zugferd.pdf", source.FileName);
        Assert.Equal([1, 2], source.Pages.Select(p => p.Image!.Width));
        Assert.All(source.Pages, p => Assert.Null(p.Text));
        Assert.Equal([150], pages.Dpis);
    }

    [Fact]
    public async Task AnXmlInvoiceIsShownAsText()
    {
        var xml = File.ReadAllBytes(TestData.Fixture("dataset/2025/spirituosen/2025-10-01_RE2503135.xml"));
        var at = (await Store("re.xml", xml)).Split('/');

        var page = Assert.Single((await svc.InvoiceSource(at[0], at[1], ct)).Pages);

        Assert.Null(page.Image);
        Assert.StartsWith("<?xml", page.Text);
        Assert.Empty(pages.Dpis);
    }

    [Fact]
    public async Task AStoredReadingGetsItsPageImagesRenderedAgain()
    {
        var reading = new List<OcrPage> { new() { Width = 1 }, new() { Width = 2 } };
        var at = (await Store("scan.pdf", "%PDF-1.4 kein Inhalt"u8.ToArray(), reading)).Split('/');

        var read = (await svc.InvoiceReading(at[0], at[1], ct)).Pages;

        Assert.Equal([1, 2], read.Select(p => p.Width));
        Assert.Equal([1, 2], read.Select(p => p.Image!.Width));
        Assert.Equal([Umsatzschaetzung.Service.Scan.Dpi], pages.Dpis);
    }

    [Fact]
    public async Task AScanIsShownTheWayItWasTurnedToBeRead()
    {
        using var sheet = Tests.Scan.Sheets.Blank(40, 30);
        sheet.SetPixel(0, 0, SKColors.Black);
        var reading = new List<OcrPage> { new() { Width = 20, Height = 15, Correction = new Correction { Scale = 0.5, Turn = 180 } } };
        var at = (await Store("scan.png", Tests.Scan.Sheets.Png(sheet), reading)).Split('/');

        var page = Assert.Single((await svc.InvoiceSource(at[0], at[1], ct)).Pages);

        using var shown = Tests.Scan.Sheets.Of(page.Image!);
        Assert.Equal((40, 30), (shown.Width, shown.Height));
        Assert.Equal(SKColors.Black, shown.GetPixel(39, 29));
        Assert.Equal(SKColors.White, shown.GetPixel(0, 0));
    }

    static OcrLine Row(string name, int y) => new()
    {
        Parsed = new InvoiceLine { Name = name },
        Cells =
        {
            [Field.Name] = new OcrWord { Text = name, Box = new Box(20, y, 30, 10) },
            [Field.LineNet] = new OcrWord { Text = "1,00", Box = new Box(120, y + 2, 20, 8) },
        },
    };

    [Fact]
    public async Task ASnippetIsTheRowAcrossTheTable()
    {
        using var sheet = Tests.Scan.Sheets.Blank(200, 100);
        var reading = new List<OcrPage> { new() { Width = 200, Height = 100, Lines = [Row("Mehl", 20), Row("Zucker", 50)] } };
        var at = (await Store("scan.png", Tests.Scan.Sheets.Png(sheet), reading)).Split('/');

        var row = await svc.InvoiceSnippet(at[0], at[1], 1, "Zucker", ct);

        // Cut from the page as it is kept: at half its resolution.
        Assert.Equal(((152 - 8) / 2, (72 - 38) / 2), row is { } r ? (r.Width, r.Height) : default);
        Assert.Null(await svc.InvoiceSnippet(at[0], at[1], 5, "Salz", ct));
    }

    [Fact]
    public async Task AnInvoiceWithoutAReadingHasNoSnippet()
    {
        var at = (await Store("re.xml", File.ReadAllBytes(TestData.Fixture("dataset/2025/spirituosen/2025-10-01_RE2503135.xml")))).Split('/');
        Assert.Null(await svc.InvoiceSnippet(at[0], at[1], 0, "x", ct));
    }
}
